// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Interop;
using IceRpc.Transports;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Represents an ice1 or ice2 request frame sent by the application.</summary>
    public sealed class OutgoingRequest : OutgoingFrame
    {
        /// <summary>The alternatives to <see cref="Endpoint"/>. It should be empty when Endpoint is null.</summary>
        public IEnumerable<Endpoint> AltEndpoints { get; set; } = ImmutableList<Endpoint>.Empty;

        /// <summary>The main target endpoint for this request.</summary>
        public Endpoint? Endpoint { get; set; }

        /// <summary>A list of endpoints this request does not want to establish a connection to, typically because a
        /// previous attempt asked the request not to.</summary>
        public IEnumerable<Endpoint> ExcludedEndpoints { get; set; } = ImmutableList<Endpoint>.Empty;

        /// <summary>The connection that will be used (or was used ) to send this request.</summary>
        public Connection? Connection { get; set; }

        /// <summary>The deadline corresponds to the request's expiration time. Once the deadline is reached, the
        /// caller is no longer interested in the response and discards the request. This deadline is sent with ice2
        /// requests but not with ice1 requests.</summary>
        /// <remarks>The source of the cancellation token given to an invoker alongside this outgoing request is
        /// expected to enforce this deadline.</remarks>
        public DateTime Deadline { get; set; }

        /// <inheritdoc/>
        public override IReadOnlyDictionary<int, ReadOnlyMemory<byte>> InitialFields { get; } =
            ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;

        /// <summary>When true, the operation is idempotent.</summary>
        public bool IsIdempotent { get; }

        /// <summary>When true and the operation returns void, the request is sent as a oneway request. Otherwise, the
        /// request is sent as a twoway request.</summary>
        public bool IsOneway { get; set; }

        /// <summary>Indicates whether or not this request has been sent.</summary>
        /// <value>When <c>true</c>, the request was sent. When <c>false</c> the request was not sent yet.</value>
        public bool IsSent { get; set; }

        /// <summary>The operation called on the service.</summary>
        public string Operation { get; set; }

        /// <summary>The path of the target service.</summary>
        public string Path
        {
            get => _path;
            set
            {
                if (Protocol == Protocol.Ice1)
                {
                    Identity = Identity.FromPath(value);
                }
                _path = value;
            }
        }

        /// <inheritdoc/>
        public override Encoding PayloadEncoding { get; private protected set; }

        /// <summary>The proxy that is sending this request.</summary>
        public IServicePrx Proxy { get; }

        /// <summary>The facet path of the target service. ice1 only.</summary>
        internal IList<string> FacetPath { get; } = ImmutableList<string>.Empty;

        /// <summary>The identity of the target service. ice1 only.</summary>
        internal Identity Identity { get; private set; }

        /// <summary>The retry policy for the request. The policy is used by the retry invoker if the request fails
        /// with a local exception. It is set by the connection code based on the context of the failure.</summary>
        internal RetryPolicy RetryPolicy { get; set; } = RetryPolicy.NoRetry;

        private string _path = "";

        /// <summary>Constructs an outgoing request from the given incoming request.</summary>
        /// <param name="proxy">The proxy sending the outgoing request.</param>
        /// <param name="request">The incoming request from which to create an outgoing request.</param>
        /// <param name="forwardFields">When true (the default), the new outgoing request uses the incoming request's
        /// fields as a fallback - all the fields lines in this fields dictionary are added before the request is sent,
        /// except for lines added by interceptors.</param>
        public OutgoingRequest(
            IServicePrx proxy,
            IncomingRequest request,
            bool forwardFields = true)
            // TODO: support stream param forwarding
            : this(proxy, request.Operation, request.Features, null)
        {
            Deadline = request.Deadline;
            IsIdempotent = request.IsIdempotent;
            IsOneway = request.IsOneway;
            PayloadEncoding = request.PayloadEncoding;

            // We forward the payload as is.
            Payload = new ReadOnlyMemory<byte>[] { request.Payload }; // TODO: temporary

            if (request.Protocol == Protocol && Protocol == Protocol.Ice2 && forwardFields)
            {
                InitialFields = request.Fields;
            }
        }

        /// <summary>Constructs an outgoing request.</summary>
        internal OutgoingRequest(
            IServicePrx proxy,
            string operation,
            ReadOnlyMemory<ReadOnlyMemory<byte>> args,
            DateTime deadline,
            Invocation? invocation = null,
            bool idempotent = false,
            bool oneway = false,
            Action<Stream>? streamDataWriter = null)
            : this(proxy,
                   operation,
                   invocation?.RequestFeatures ?? FeatureCollection.Empty,
                   streamDataWriter)
        {
            Deadline = deadline;
            IsOneway = oneway || (invocation?.IsOneway ?? false);
            IsIdempotent = idempotent || (invocation?.IsIdempotent ?? false);
            Payload = args;
        }

        /// <inheritdoc/>
        internal override IncomingFrame ToIncoming() => new IncomingRequest(this);

        /// <inheritdoc/>
        internal override void WriteHeader(OutputStream ostr)
        {
            Debug.Assert(ostr.Encoding == Protocol.GetEncoding());

            IDictionary<string, string> context = Features.GetContext();

            if (Protocol == Protocol.Ice2)
            {
                OutputStream.Position start = ostr.StartFixedLengthSize(2);

                // DateTime.MaxValue represents an infinite deadline and it is encoded as -1
                var requestHeaderBody = new Ice2RequestHeaderBody(
                    Path,
                    Operation,
                    idempotent: IsIdempotent ? true : null,
                    priority: null,
                    deadline: Deadline == DateTime.MaxValue ? -1 :
                        (long)(Deadline - DateTime.UnixEpoch).TotalMilliseconds);

                requestHeaderBody.IceWrite(ostr);

                if (InitialFields.ContainsKey((int)Ice2FieldKey.Context) || context.Count > 0)
                {
                    // Writes or overrides context
                    FieldsOverride[(int)Ice2FieldKey.Context] =
                        ostr => ostr.WriteDictionary(context,
                                                     OutputStream.IceWriterFromString,
                                                     OutputStream.IceWriterFromString);
                }
                // else context remains empty (not set)

                WriteFields(ostr);
                PayloadEncoding.IceWrite(ostr);
                ostr.WriteSize(PayloadSize);
                ostr.EndFixedLengthSize(start, 2);
            }
            else
            {
                Debug.Assert(Protocol == Protocol.Ice1);
                var requestHeader = new Ice1RequestHeader(
                    Identity,
                    FacetPath,
                    Operation,
                    IsIdempotent ? OperationMode.Idempotent : OperationMode.Normal,
                    context,
                    encapsulationSize: PayloadSize + 6,
                    PayloadEncoding);
                requestHeader.IceWrite(ostr);
            }
        }

        private OutgoingRequest(
            IServicePrx proxy,
            string operation,
            FeatureCollection features,
            Action<Stream>? streamDataWriter)
            : base(proxy.Protocol, features, streamDataWriter)
        {
            AltEndpoints = proxy.AltEndpoints;
            Connection = proxy.Connection;
            Endpoint = proxy.Endpoint;
            Proxy = proxy;

            if (Protocol == Protocol.Ice1)
            {
                FacetPath = proxy.Impl.FacetPath;
                Identity = proxy.Impl.Identity;
            }

            Operation = operation;
            Path = proxy.Path;
            PayloadEncoding = proxy.Encoding;
            Payload = ReadOnlyMemory<ReadOnlyMemory<byte>>.Empty;
        }
    }
}
