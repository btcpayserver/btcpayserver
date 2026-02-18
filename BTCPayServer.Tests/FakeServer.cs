using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Tests
{
    public class FakeServer : IDisposable
    {
        IHost host;
        readonly SemaphoreSlim semaphore;
        readonly CancellationTokenSource cts = new CancellationTokenSource();
        public FakeServer()
        {
            _channel = Channel.CreateUnbounded<HttpContext>();
            semaphore = new SemaphoreSlim(0);
        }

        readonly Channel<HttpContext> _channel;
        public async Task Start()
        {
            host = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                        .UseKestrel()
                        .UseUrls("http://127.0.0.1:0")
                        .Configure(app =>
                        {
                            app.Run(async ctx =>
                            {
                                await _channel.Writer.WriteAsync(ctx);
                                await semaphore.WaitAsync(cts.Token);
                            });
                        });
                })
                .Build();
            await host.StartAsync();
            var port = new Uri(host.GetServerFeatures<IServerAddressesFeature>().Addresses.First(), UriKind.Absolute)
                .Port;
            ServerUri = new Uri($"http://127.0.0.1:{port}/");
        }

        public Uri ServerUri { get; set; }

        public void Done()
        {
            semaphore.Release();
        }

        public async Task Stop()
        {
            await host.StopAsync();
        }
        public void Dispose()
        {
            cts.Dispose();
            host?.Dispose();
            semaphore.Dispose();
        }

        public async Task<HttpContext> GetNextRequest(CancellationToken cancellationToken = default)
        {
            return await _channel.Reader.ReadAsync(cancellationToken);
        }
    }
}
