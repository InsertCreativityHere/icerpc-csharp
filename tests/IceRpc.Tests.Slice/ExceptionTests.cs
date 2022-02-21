// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture("icerpc")]
    public sealed class ExceptionTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly ExceptionOperationsPrx _prx;

        public ExceptionTests(string protocol)
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(protocol)
                .AddTransient<IDispatcher, ExceptionOperations>()
                .BuildServiceProvider();
            _prx = ExceptionOperationsPrx.FromConnection(_serviceProvider.GetRequiredService<Connection>());
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public void Exception_Constructors()
        {
            var a = new MyExceptionA(RetryPolicy.NoRetry);
            Assert.AreEqual(RetryPolicy.NoRetry, a.RetryPolicy);

            a = new MyExceptionA(RetryPolicy.OtherReplica);
            Assert.AreEqual(RetryPolicy.OtherReplica, a.RetryPolicy);

            a = new MyExceptionA(RetryPolicy.AfterDelay(TimeSpan.FromSeconds(1)));
            Assert.AreEqual(RetryPolicy.AfterDelay(TimeSpan.FromSeconds(1)), a.RetryPolicy);

            a = new MyExceptionA();
            Assert.AreEqual(RetryPolicy.NoRetry, a.RetryPolicy);

            Assert.AreEqual(10, new MyExceptionA(10).M1);

            var b = new MyExceptionB("my exception B", 20, retryPolicy: RetryPolicy.AfterDelay(TimeSpan.FromSeconds(1)));
            a = new MyExceptionA("my exception A", 10, b, RetryPolicy.AfterDelay(TimeSpan.FromSeconds(1)));
            Assert.AreEqual("my exception A", a.Message);
            Assert.AreEqual(10, a.M1);
            Assert.That(a, Is.Not.Null);
            Assert.AreEqual(b, a.InnerException);
            Assert.AreEqual(RetryPolicy.AfterDelay(TimeSpan.FromSeconds(1)), a.RetryPolicy);
        }

        [Test]
        public async Task Exception_Member_Operations()
        {
            // Passing exceptions as a data member is not supported with the 1.1 encoding.
            if (_prx.Proxy.Encoding == Encoding.Slice11)
            {
                return;
            }

            MyExceptionA a = await _prx.OpMyExceptionAAsync(new MyExceptionA(-79));
            Assert.That(a, Is.EqualTo(new MyExceptionA(-79)));
        }

        [Test]
        public void Exception_Throw_Operations()
        {
            MyExceptionA? a = Assert.ThrowsAsync<MyExceptionA>(async () => await _prx.ThrowAAsync(10));
            Assert.That(a, Is.Not.Null);
            Assert.AreEqual(10, a!.M1);

            a = Assert.ThrowsAsync<MyExceptionA>(async () => await _prx.ThrowAorBAsync(10));
            Assert.That(a, Is.Not.Null);
            Assert.AreEqual(10, a!.M1);

            MyExceptionB? b = Assert.ThrowsAsync<MyExceptionB>(async () => await _prx.ThrowAorBAsync(0));
            Assert.That(b, Is.Not.Null);
            Assert.AreEqual(0, b!.M1);

            RemoteException? remoteEx =
                Assert.ThrowsAsync<RemoteException>(async () => await _prx.ThrowRemoteExceptionAsync());

            Assert.That(remoteEx, Is.Not.Null);
            if (_prx.Proxy.Protocol == Protocol.IceRpc)
            {
                Assert.AreEqual("some message", remoteEx!.Message);
            }
        }

        public class ExceptionOperations : Service, IExceptionOperations
        {
            public ValueTask<MyExceptionA> OpMyExceptionAAsync(
                MyExceptionA p1,
                Dispatch dispatch,
                CancellationToken cancel) => new(p1);

            public ValueTask ThrowAAsync(int a, Dispatch dispatch, CancellationToken cancel) => throw new MyExceptionA(a);
            public ValueTask ThrowAorBAsync(int a, Dispatch dispatch, CancellationToken cancel)
            {
                if (a > 0)
                {
                    throw new MyExceptionA(a);
                }
                else
                {
                    throw new MyExceptionB(a);
                }
            }

            public ValueTask ThrowRemoteExceptionAsync(Dispatch dispatch, CancellationToken cancel) =>
                throw new RemoteException("some message");
        }
    }
}
