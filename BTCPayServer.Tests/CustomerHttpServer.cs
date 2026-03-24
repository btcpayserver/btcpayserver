using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Tests
{
    public class CustomServer : IDisposable
    {
        readonly IHost _host;
        readonly CancellationTokenSource _closed = new CancellationTokenSource();
        readonly Channel<JObject> _requests = Channel.CreateUnbounded<JObject>();
        public CustomServer()
        {
            var port = Utils.FreeTcpPort();
            _host = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                        .UseKestrel()
                        .UseUrls($"http://127.0.0.1:{port}")
                        .Configure(app =>
                        {
                            app.Run(async req =>
                            {
                                using var reader = new StreamReader(req.Request.Body);
                                var body = await reader.ReadToEndAsync();

                                await _requests.Writer.WriteAsync(
                                    JsonConvert.DeserializeObject<JObject>(body),
                                    _closed.Token);

                                req.Response.StatusCode = 200;
                            });
                        });
                })
                .Build();
            _host.Start();
        }

        public Uri GetUri()
        {
            return new Uri(_host.GetServerFeatures<IServerAddressesFeature>().Addresses.First());
        }

        public async Task<JObject> GetNextRequest()
        {
            using var cancellation = new CancellationTokenSource(2000000);
            try
            {
                JObject req;
                while (!await _requests.Reader.WaitToReadAsync(cancellation.Token) ||
                    !_requests.Reader.TryRead(out req))
                {

                }
                return req;
            }
            catch (TaskCanceledException)
            {
                throw new Xunit.Sdk.XunitException("Callback to the webserver was expected, check if the callback url is accessible from internet");
            }
        }

        public void Dispose()
        {
            _closed.Cancel();
            _host.Dispose();
        }
    }
}
