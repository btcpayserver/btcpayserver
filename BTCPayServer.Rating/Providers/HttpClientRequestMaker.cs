using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ExchangeSharp;
using System.Threading;

namespace BTCPayServer.Services.Rates
{
    internal class HttpClientRequestMaker : IAPIRequestMaker
    {
        class InternalHttpWebRequest : IHttpWebRequest
        {
            internal readonly HttpWebRequest Request;

            public Uri RequestUri => Request.RequestUri;

            public string Method
            {
                get
                {
                    return Request.Method;
                }
                set
                {
                    Request.Method = value;
                }
            }

            public int Timeout
            {
                get
                {
                    return Request.Timeout;
                }
                set
                {
                    Request.Timeout = value;
                }
            }

            public int ReadWriteTimeout
            {
                get
                {
                    return Request.ReadWriteTimeout;
                }
                set
                {
                    Request.ReadWriteTimeout = value;
                }
            }

            public InternalHttpWebRequest(Uri fullUri)
            {
                Request = ((WebRequest.Create(fullUri) as HttpWebRequest) ?? throw new NullReferenceException("Failed to create HttpWebRequest"));
                Request.KeepAlive = false;
            }

            public void AddHeader(string header, string value)
            {
                switch (header.ToStringLowerInvariant())
                {
                    case "content-type":
                        Request.ContentType = value;
                        break;
                    case "content-length":
                        Request.ContentLength = value.ConvertInvariant<long>(0L);
                        break;
                    case "user-agent":
                        Request.UserAgent = value;
                        break;
                    case "accept":
                        Request.Accept = value;
                        break;
                    case "connection":
                        Request.Connection = value;
                        break;
                    default:
                        Request.Headers[header] = value;
                        break;
                }
            }

            public Task WriteAllAsync(byte[] data, int index, int length)
            {
                throw new NotImplementedException();
            }

            public HttpRequestMessage ToHttpRequestMessage()
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Get, Request.RequestUri);
                CopyHeadersFrom(httpRequest, Request);
                return httpRequest;
            }

            internal void CopyHeadersFrom(HttpRequestMessage message, HttpWebRequest request)
            {
                foreach (string headerName in request.Headers)
                {
                    string[] headerValues = request.Headers.GetValues(headerName);
                    if (!message.Headers.TryAddWithoutValidation(headerName, headerValues))
                    {
                        if (message.Content != null)
                            message.Content.Headers.TryAddWithoutValidation(headerName, headerValues);
                    }
                }
            }
        }
        class InternalHttpWebResponse : IHttpWebResponse
        {
            public InternalHttpWebResponse(HttpResponseMessage httpResponseMessage)
            {
                var headers = new Dictionary<string, List<string>>();
                foreach (var h in httpResponseMessage.Headers)
                {
                    if (!headers.TryGetValue(h.Key, out var list))
                    {
                        list = new List<string>();
                        headers.Add(h.Key, list);
                    }
                    list.AddRange(h.Value);
                }
                Headers = new Dictionary<string, IReadOnlyList<string>>(headers.Count);
                foreach (var item in headers)
                {
                    Headers.Add(item.Key, item.Value.AsReadOnly());
                }
            }
            public Dictionary<string, IReadOnlyList<string>> Headers { get; }
            static IReadOnlyList<string> Empty = new List<string>().AsReadOnly();
            public IReadOnlyList<string> GetHeader(string name)
            {
                Headers.TryGetValue(name, out var list);
                return list ?? Empty;
            }
        }
        private readonly IAPIRequestHandler api;
        private readonly HttpClient _httpClient;
        private readonly CancellationToken _cancellationToken;

        public HttpClientRequestMaker(IAPIRequestHandler api, HttpClient httpClient, CancellationToken cancellationToken)
        {
            if (api == null)
                throw new ArgumentNullException(nameof(api));
            if (httpClient == null)
                throw new ArgumentNullException(nameof(httpClient));
            this.api = api;
            _httpClient = httpClient;
            _cancellationToken = cancellationToken;
        }
        public Action<IAPIRequestMaker, RequestMakerState, object> RequestStateChanged
        {
            get;
            set;
        }

        public async Task<string> MakeRequestAsync(string url, string baseUrl = null, Dictionary<string, object> payload = null, string method = null)
        {
            await default(SynchronizationContextRemover);
            await api.RateLimit.WaitToProceedAsync();
            if (url[0] != '/')
            {
                url = "/" + url;
            }
            string uri2 = (baseUrl ?? api.BaseUrl) + url;
            if (method == null)
            {
                method = api.RequestMethod;
            }
            Uri uri = api.ProcessRequestUrl(new UriBuilder(uri2), payload, method);
            InternalHttpWebRequest request = new InternalHttpWebRequest(uri)
            {
                Method = method
            };
            request.AddHeader("content-type", api.RequestContentType);
            request.AddHeader("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/79.0.3945.117 Safari/537.36");
            int num3 = request.Timeout = (request.ReadWriteTimeout = (int)api.RequestTimeout.TotalMilliseconds);
            await api.ProcessRequestAsync(request, payload);
            try
            {
                RequestStateChanged?.Invoke(this, RequestMakerState.Begin, uri.AbsoluteUri);
                using var webHttpRequest = request.ToHttpRequestMessage();
                using var webHttpResponse = await _httpClient.SendAsync(webHttpRequest, _cancellationToken);
                string text = await webHttpResponse.Content.ReadAsStringAsync();
                if (!webHttpResponse.IsSuccessStatusCode)
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        throw new APIException($"{webHttpResponse.StatusCode.ConvertInvariant<int>(0)} - {webHttpResponse.StatusCode}");
                    }
                    throw new APIException(text);
                }
                api.ProcessResponse(new InternalHttpWebResponse(webHttpResponse));
                // local reference to handle delegate becoming null, extended discussion here:
                // https://github.com/btcpayserver/btcpayserver/commit/00747906849f093712c3907c99404c55b3defa66#r37022103
                var requestStateChanged = RequestStateChanged;
                if (requestStateChanged != null)
                {
                    requestStateChanged(this, RequestMakerState.Finished, text);
                    return text;
                }
                return text;
            }
            catch (Exception arg)
            {
                RequestStateChanged?.Invoke(this, RequestMakerState.Error, arg);
                throw;
            }
        }
    }
}
