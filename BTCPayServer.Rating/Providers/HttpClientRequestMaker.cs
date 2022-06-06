using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ExchangeSharp;

namespace BTCPayServer.Services.Rates
{
    internal class HttpClientRequestMaker : IAPIRequestMaker
    {
#nullable enable
        internal class InternalHttpWebRequest : IHttpWebRequest
        {
            internal readonly HttpRequestMessage Request;
            private string? contentType;

            public InternalHttpWebRequest(string method, Uri fullUri)
            {
                Request = new HttpRequestMessage(new HttpMethod(method), fullUri);
            }

            public void AddHeader(string header, string value)
            {
                switch (header.ToLowerInvariant())
                {
                    case "content-type":
                        contentType = value;
                        break;
                    default:
                        Request.Headers.TryAddWithoutValidation(header, value);
                        break;
                }
            }

            public Uri RequestUri
            {
                get { return Request.RequestUri!; }
            }

            public string Method
            {
                get { return Request.Method.Method; }
                set { Request.Method = new HttpMethod(value); }
            }

            public int Timeout { get; set; }

            public int ReadWriteTimeout
            {
                get => Timeout;
                set => Timeout = value;
            }


            public Task WriteAllAsync(byte[] data, int index, int length)
            {
                Request.Content = new ByteArrayContent(data, index, length);
                Request.Content.Headers.Add("content-type", contentType);
                return Task.CompletedTask;
            }
        }
#nullable restore
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
            static readonly IReadOnlyList<string> Empty = new List<string>().AsReadOnly();
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
            ArgumentNullException.ThrowIfNull(api);
            ArgumentNullException.ThrowIfNull(httpClient);
            this.api = api;
            _httpClient = httpClient;
            _cancellationToken = cancellationToken;
        }
        public Action<IAPIRequestMaker, RequestMakerState, object> RequestStateChanged
        {
            get;
            set;
        }

        public async Task<IAPIRequestMaker.RequestResult<string>> MakeRequestAsync(string url, string baseUrl = null, Dictionary<string, object> payload = null, string method = null)
        {
            await default(SynchronizationContextRemover);
            await api.RateLimit.WaitToProceedAsync();
            if (url[0] != '/')
            {
                url = "/" + url;
            }

            // prepare the request
            string fullUrl = (baseUrl ?? api.BaseUrl) + url;
            method ??= api.RequestMethod;
            Uri uri = api.ProcessRequestUrl(new UriBuilder(fullUrl), payload, method);
            InternalHttpWebRequest request = new InternalHttpWebRequest(method, uri);
            request.AddHeader("accept-language", "en-US,en;q=0.5");
            request.AddHeader("content-type", api.RequestContentType);
            request.AddHeader("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/96.0.4664.45 Safari/537.36");
            request.Timeout = (int)api.RequestTimeout.TotalMilliseconds;
            await api.ProcessRequestAsync(request, payload);

            // send the request
            HttpResponseMessage response = null;
            string responseString;
            using var cancel = new CancellationTokenSource(request.Timeout);
            try
            {
                RequestStateChanged?.Invoke(this, RequestMakerState.Begin, uri.AbsoluteUri);// when start make a request we send the uri, this helps developers to track the http requests.
                response = await _httpClient.SendAsync(request.Request, cancel.Token);
                if (response == null)
                {
                    throw new APIException("Unknown response from server");
                }
                responseString = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.Created)
                {
                    // 404 maybe return empty responseString
                    if (string.IsNullOrWhiteSpace(responseString))
                    {
                        throw new APIException(string.Format("{0} - {1}", response.StatusCode.ConvertInvariant<int>(), response.StatusCode));
                    }

                    throw new APIException(responseString);
                }

                api.ProcessResponse(new InternalHttpWebResponse(response));
                RequestStateChanged?.Invoke(this, RequestMakerState.Finished, responseString);
            }
            catch (OperationCanceledException ex) when (cancel.IsCancellationRequested)
            {
                RequestStateChanged?.Invoke(this, RequestMakerState.Error, ex);
                throw new TimeoutException("APIRequest timeout", ex);
            }
            catch (Exception ex)
            {
                RequestStateChanged?.Invoke(this, RequestMakerState.Error, ex);
                throw;
            }
            finally
            {
                response?.Dispose();
            }
            return new IAPIRequestMaker.RequestResult<string>()
            {
                Response = responseString,
                HTTPHeaderDate = response.Headers.Date
            };
        }
    }
}
