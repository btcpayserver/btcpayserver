using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Tests
{
    public class RawHttpServer : IDisposable
    {
        public class RawRequest
        {
            public RawRequest(TaskCompletionSource<bool> taskCompletion)
            {
                TaskCompletion = taskCompletion;
            }
            public HttpContext HttpContext { get; set; }
            public TaskCompletionSource<bool> TaskCompletion { get; }

            public void Complete()
            {
                TaskCompletion.SetResult(true);
            }
        }
        readonly IWebHost _Host = null;
        readonly CancellationTokenSource _Closed = new CancellationTokenSource();
        readonly Channel<RawRequest> _Requests = Channel.CreateUnbounded<RawRequest>();
        public RawHttpServer()
        {
            var port = Utils.FreeTcpPort();
            _Host = new WebHostBuilder()
                .Configure(app =>
                {
                    app.Run(req =>
                    {
                        var cts = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        _Requests.Writer.TryWrite(new RawRequest(cts)
                        {
                            HttpContext = req
                        });
                        return cts.Task;
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

        public async Task<RawRequest> GetNextRequest()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource(20 * 1000))
            {
                try
                {
                    RawRequest req = null;
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
        }

        public void Dispose()
        {
            _Closed.Cancel();
            _Host.Dispose();
        }
    }
}
