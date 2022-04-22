// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.Collections.Immutable;

namespace IceRpc.Tests;

[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public sealed class IceRpcProtocolConnectionTests
{
    public static IEnumerable<TestCaseData> ExceptionIsEncodedAsDispatchExceptionSource
    {
        get
        {
            yield return new TestCaseData(new InvalidDataException("invalid data"), DispatchErrorCode.InvalidData);
            // Slice1 only exception will get encoded as unhandled exception with Slice2
            yield return new TestCaseData(new MyDerivedException(), DispatchErrorCode.UnhandledException);
            yield return new TestCaseData(new InvalidOperationException(), DispatchErrorCode.UnhandledException);
        }
    }

    /// <summary>Verifies that canceling connection shutdown cancels pending invocations.</summary>
    [Test]
    public async Task Canceling_shutdown_cancels_pending_invocations()
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerConnectionOptions(new ConnectionOptions()
            {
                Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    start.Release();
                    await hold.WaitAsync(cancel);
                    return new OutgoingResponse(request);
                })
            })
            .BuildServiceProvider();

        var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        sut.Server.PeerShutdownInitiated = message => _ = sut.Server.ShutdownAsync(message);
        _ = sut.Client.AcceptRequestsAsync();
        var serverAcceptTask = sut.Server.AcceptRequestsAsync();
        var response = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)));
        await start.WaitAsync(); // Wait for the dispatch to start

        // Act
        var shutdownTask = sut.Client.ShutdownAsync("", new CancellationToken(canceled: true));

        // Assert
        Assert.That(async () => await response, Throws.TypeOf<OperationCanceledException>());
        Assert.That(async () => await shutdownTask, Throws.Nothing);
        hold.Release();
    }

    /// <summary>Verifies that exception thrown by the dispatcher are correctly mapped to a DispatchException with the
    /// expected error code.</summary>
    /// <param name="thrownException">The exception to throw by the dispatcher.</param>
    /// <param name="errorCode">The expected <see cref="DispatchErrorCode"/>.</param>
    [Test, TestCaseSource(nameof(ExceptionIsEncodedAsDispatchExceptionSource))]
    public async Task Exception_is_encoded_as_a_dispatch_exception(
        Exception thrownException,
        DispatchErrorCode errorCode)
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancel) => throw thrownException);

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();

        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Server.AcceptRequestsAsync();
        _ = sut.Client.AcceptRequestsAsync();

        // Act
        var response = await sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)));

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
        var exception = await response.DecodeFailureAsync() as DispatchException;
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception.ErrorCode, Is.EqualTo(errorCode));
    }

    /// <summary>Ensures that the connection fields are correctly exchanged on the protocol connection
    /// initialization.</summary>
    [Test]
    public async Task Initialize_peer_fields()
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerConnectionOptions(new ConnectionOptions()
            {
                Fields = new Dictionary<ConnectionFieldKey, OutgoingFieldValue>()
                        .With(ConnectionFieldKey.MaxHeaderSize, (ref SliceEncoder encoder) => encoder.EncodeInt32(56))
            })
            .UseClientConnectionOptions(new ConnectionOptions()
            {
                Fields = new Dictionary<ConnectionFieldKey, OutgoingFieldValue>()
                        .With(ConnectionFieldKey.MaxHeaderSize, (ref SliceEncoder encoder) => encoder.EncodeInt32(34))
                        .With((ConnectionFieldKey)10, (ref SliceEncoder encoder) => encoder.EncodeInt32(38))
            })
            .BuildServiceProvider();

        // Act
        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(sut.Server.PeerFields, Has.Count.EqualTo(2));
            Assert.That(sut.Client.PeerFields, Has.Count.EqualTo(1));
            Assert.That(DecodeField(sut.Server.PeerFields, ConnectionFieldKey.MaxHeaderSize), Is.EqualTo(34));
            Assert.That(DecodeField(sut.Server.PeerFields, (ConnectionFieldKey)10), Is.EqualTo(38));
            Assert.That(DecodeField(sut.Client.PeerFields, ConnectionFieldKey.MaxHeaderSize), Is.EqualTo(56));
        });

        static int DecodeField(
            ImmutableDictionary<ConnectionFieldKey, ReadOnlySequence<byte>> fields,
            ConnectionFieldKey key) =>
            fields.DecodeValue(key, (ref SliceDecoder decoder) => decoder.DecodeInt32());
    }

    /// <summary>Ensures that the request payload is completed if the request fields are invalid.</summary>
    [Test]
    public async Task Payload_completed_on_invalid_request_fields([Values(true, false)] bool isOneway)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc))
        {
            IsOneway = isOneway,
            Payload = payloadDecorator
        };
        request.Fields = request.Fields.With(
            RequestFieldKey.Idempotent,
            (ref SliceEncoder encoder) => throw new NotSupportedException("invalid request fields"));

        // Act
        _ = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await payloadDecorator.Completed, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the response payload is completed if the response fields are invalid.</summary>
    [Test]
    public async Task Payload_completed_on_invalid_response_fields()
    {
        // Arrange
        var payloadDecorator = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancel) =>
            {
                var response = new OutgoingResponse(request)
                {
                    Payload = payloadDecorator
                };
                response.Fields = response.Fields.With(
                    ResponseFieldKey.CompressionFormat,
                    (ref SliceEncoder encoder) => throw new NotSupportedException("invalid request fields"));
                return new(response);
            });

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)));

        // Assert
        Assert.That(await payloadDecorator.Completed, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload stream is completed on valid request.</summary>
    [Test]
    public async Task PayloadStream_completed_on_valid_request([Values(true, false)] bool isOneway)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        var payloadStreamDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc))
        {
            IsOneway = isOneway,
            PayloadStream = payloadStreamDecorator
        };

        // Act
        _ = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await payloadStreamDecorator.Completed, Is.Null);
    }

    /// <summary>Ensures that the request payload is completed if the payload stream is invalid.</summary>
    [Test]
    public async Task PayloadStream_completed_on_invalid_request_payload([Values(true, false)] bool isOneway)
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        var payloadStreamDecorator = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc))
        {
            IsOneway = isOneway,
            PayloadStream = payloadStreamDecorator
        };

        // Act
        _ = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await payloadStreamDecorator.Completed, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload stream is completed if the connection is shutdown.</summary>
    [Test]
    public async Task PayloadStream_completed_on_request_when_connection_is_shutdown()
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Client.ShutdownAsync("", default);

        var payloadStreamDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc))
        {
            PayloadStream = payloadStreamDecorator
        };

        // Act
        Task<IncomingResponse> invokeTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.Multiple(async () =>
        {
            Assert.ThrowsAsync<ConnectionClosedException>(() => invokeTask);
            Assert.That(await payloadStreamDecorator.Completed, Is.InstanceOf<ConnectionClosedException>());
        });
    }

    /// <summary>Ensures that the response payload stream is completed on a valid response.</summary>
    [Test]
    public async Task PayloadStream_completed_on_valid_response()
    {
        // Arrange
        var payloadStreamDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancel) =>
                new(new OutgoingResponse(request)
                {
                    PayloadStream = payloadStreamDecorator
                }));

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)));

        // Assert
        Assert.That(await payloadStreamDecorator.Completed, Is.Null);
    }

    /// <summary>Ensures that the response payload is completed on an invalid response payload stream.</summary>
    [Test]
    public async Task PayloadStream_completed_on_invalid_response_payload()
    {
        // Arrange
        var payloadStreamDecorator = new PayloadPipeReaderDecorator(InvalidPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancel) =>
                new(new OutgoingResponse(request)
                {
                    PayloadStream = payloadStreamDecorator
                }));

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)));

        // Assert
        Assert.That(await payloadStreamDecorator.Completed, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload writer is completed on an invalid request.</summary>
    /// <remarks>This test only works with the icerpc protocol since it relies on reading the payload after the payload
    /// writer is created.</remarks>
    [Test]
    public async Task PayloadWriter_completed_with_invalid_request()
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Server.AcceptRequestsAsync();

        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc))
        {
            Payload = InvalidPipeReader.Instance
        };
        var payloadWriterSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
        request.Use(writer =>
            {
                var payloadWriterDecorator = new PayloadPipeWriterDecorator(writer);
                payloadWriterSource.SetResult(payloadWriterDecorator);
                return payloadWriterDecorator;
            });

        // Act
        _ = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Ensures that the request payload writer is completed on an invalid response.</summary>
    /// <remarks>This test only works with the icerpc protocol since it relies on reading the payload after the payload
    /// writer is created.</remarks>
    [Test]
    public async Task PayloadWriter_completed_with_invalid_response()
    {
        // Arrange
        var payloadWriterSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
        var dispatcher = new InlineDispatcher((request, cancel) =>
            {
                var response = new OutgoingResponse(request)
                {
                    Payload = InvalidPipeReader.Instance
                };
                response.Use(writer =>
                    {
                        var payloadWriterDecorator = new PayloadPipeWriterDecorator(writer);
                        payloadWriterSource.SetResult(payloadWriterDecorator);
                        return payloadWriterDecorator;
                    });
                return new(response);
            });

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)));

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>Verifies that canceling connection shutdown cancels pending invocations.</summary>
    [Test]
    public async Task Shutdown_waits_for_pending_invocations_to_finish()
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.IceRpc)
            .UseServerConnectionOptions(new ConnectionOptions()
            {
                Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    start.Release();
                    await hold.WaitAsync(cancel);
                    return new OutgoingResponse(request);
                })
            })
            .BuildServiceProvider();

        var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        sut.Server.PeerShutdownInitiated = message => _ = sut.Server.ShutdownAsync(message);
        var invokeTask = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)));
        var serverAcceptTask = sut.Server.AcceptRequestsAsync();
        await start.WaitAsync(); // Wait for the dispatch to start

        // Act
        var shutdownTask = sut.Client.ShutdownAsync("", default);

        // Assert
        Assert.That(invokeTask.IsCompleted, Is.False);
        Assert.That(shutdownTask.IsCompleted, Is.False);
        hold.Release();
        Assert.That(async () => await invokeTask, Throws.Nothing);
        Assert.That(async () => await shutdownTask, Throws.Nothing);
    }
}
