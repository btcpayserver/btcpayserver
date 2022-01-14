using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Tests
{
    public class CustomServer : IDisposable
    {
        readonly IWebHost _Host = null;
        readonly CancellationTokenSource _Closed = new CancellationTokenSource();
        readonly Channel<JObject> _Requests = Channel.CreateUnbounded<JObject>();
        public CustomServer()
        {
            var port = Utils.FreeTcpPort();
            _Host = new WebHostBuilder()
                .Configure(app =>
                {
                    app.Run(async req =>
                    {
                        await _Requests.Writer.WriteAsync(JsonConvert.DeserializeObject<JObject>(await new StreamReader(req.Request.Body).ReadToEndAsync()), _Closed.Token);
                        req.Response.StatusCode = 200;
                    });
                })
                .UseKestrel()
                .UseUrls("http://127.0.0.1:" + port)
                .Build();
            _Host.Start();
        }

        public Uri GetUri()
        {
            return new Uri(_Host.ServerFeatures.Get<IServerAddressesFeature>().Addresses.First());
        }

        public async Task<JObject> GetNextRequest()
        {
            using CancellationTokenSource cancellation = new CancellationTokenSource(2000000);
            try
            {
                JObject req = null;
                while (!await _Requests.Reader.WaitToReadAsync(cancellation.Token) ||
                    !_Requests.Reader.TryRead(out req))
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
            _Closed.Cancel();
            _Host.Dispose();
        }
    }
}
