using Microsoft.AspNetCore.Hosting;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace BTCPayServer.Tests
{
    public class CustomServer : IDisposable
    {
        TaskCompletionSource<bool> _Evt = null;
        IWebHost _Host = null;
        CancellationTokenSource _Closed = new CancellationTokenSource();
        public CustomServer()
        {
            var port = Utils.FreeTcpPort();
            _Host = new WebHostBuilder()
                .Configure(app =>
                {
                    app.Run(req =>
                    {
                        while (_Act == null)
                        {
                            Thread.Sleep(10);
                            _Closed.Token.ThrowIfCancellationRequested();
                        }
                        _Act(req);
                        _Act = null;
                        _Evt.TrySetResult(true);
                        req.Response.StatusCode = 200;
                        return Task.CompletedTask;
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

        Action<HttpContext> _Act;
        public void ProcessNextRequest(Action<HttpContext> act)
        {
            var source = new TaskCompletionSource<bool>();
            CancellationTokenSource cancellation = new CancellationTokenSource(20000);
            cancellation.Token.Register(() => source.TrySetCanceled());
            source = new TaskCompletionSource<bool>();
            _Evt = source;
            _Act = act;
            try
            {
                _Evt.Task.GetAwaiter().GetResult();
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
