// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Demo;

public static class Program
{
    public static void Main(string[] args) => CreateHostBuilder(args).Build().Run();

    /// <summary>Creates the HostBuilder used to build the .NET Generic Host.</summary>
    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            // Set the content root path to the build directory of the server (e.g.: Client/bin/Debug/net6.0)
            .UseContentRoot(AppContext.BaseDirectory)

            // Configures the .NET Generic Host services.
            .ConfigureServices((hostContext, services) =>
            {
                // Add the ClientHostedService to the hosted services of the .NET Generic Host.
                services.AddHostedService<ClientHostedService>();

                // Bind the client connection options to the "appsettings.json" configuration "Client" section,
                // and add a Configure callback to configure its authentication options.
                services
                    .AddOptions<ClientConnectionOptions>()
                    .Bind(hostContext.Configuration.GetSection("Client"))
                    .Configure<IOptions<SslClientAuthenticationOptions>>((options, clientAuthenticationOptions) =>
                        options.ClientAuthenticationOptions = clientAuthenticationOptions.Value);

                services
                    .AddOptions<SslClientAuthenticationOptions>()
                    .Configure(options =>
                    {
                        // Configure the authentication options
                        var rootCA = new X509Certificate2(
                            hostContext.Configuration.GetValue<string>("CertificateAuthoritiesFile"));

                        // A certificate validation callback that uses the configured certificate authorities file
                        // to validate the peer certificates.
                        options.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                        {
                            using var customChain = new X509Chain();
                            customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                            customChain.ChainPolicy.DisableCertificateDownloads = true;
                            customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                            customChain.ChainPolicy.CustomTrustStore.Add(rootCA);
                            return customChain.Build((X509Certificate2)certificate!);
                        };
                    });
                services.AddSingleton(sp => new ActivitySource("IceRpc"));

                // Add an IInvoker service.
                services.AddSingleton<IInvoker>(serviceProvider =>
                {
                    // The invoker is a pipeline configured with the logger and telemetry interceptors. The
                    // interceptors use the logger factory provided by the .NET Generic Host.
                    return new Pipeline()
                        .UseLogger(serviceProvider.GetRequiredService<ILoggerFactory>())
                        .UseTelemetry(serviceProvider.GetRequiredService<ActivitySource>());
                });

                services.AddIceRpcClientConnection();

                services.AddSingleton<IHelloPrx>(serviceProvider =>
                    HelloPrx.FromConnection(
                        serviceProvider.GetRequiredService<ClientConnection>(),
                        path: "/hello",
                        invoker: serviceProvider.GetRequiredService<IInvoker>()));
            });

    /// <summary>The hosted client service is ran and managed by the .NET Generic Host</summary>
    private class ClientHostedService : BackgroundService
    {
        // The host application lifetime is used to stop the .NET Generic Host.
        private readonly IHostApplicationLifetime _applicationLifetime;

        // A proxy to the remote Hello service.
        private readonly IHelloPrx _hello;

        public ClientHostedService(IHelloPrx hello, IHostApplicationLifetime applicationLifetime)
        {
            _applicationLifetime = applicationLifetime;
            _hello = hello;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            try
            {
                Console.Write("To say hello to the server, type your name: ");
                if (Console.ReadLine() is string name)
                {
                    Console.WriteLine(await _hello.SayHelloAsync(name, cancel: cancellationToken));
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine($"failed to connect to the server:\n{exception}");
            }

            // Stop the generic host once the invocation is done.
            _applicationLifetime.StopApplication();
        }
    }
}
