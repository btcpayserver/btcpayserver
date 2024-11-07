using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    private readonly string _apiKey;
    private readonly Uri _btcpayHost;
    private readonly string _username;
    private readonly string _password;
    protected readonly HttpClient _httpClient;
    public Uri Host => _btcpayHost;

    public string APIKey => _apiKey;

    public BTCPayServerClient(Uri btcpayHost, HttpClient httpClient = null)
    {
        if (btcpayHost == null) throw new ArgumentNullException(nameof(btcpayHost));
        _btcpayHost = btcpayHost;
        _httpClient = httpClient ?? new HttpClient();
    }

    public BTCPayServerClient(Uri btcpayHost, string APIKey, HttpClient httpClient = null)
    {
        _apiKey = APIKey;
        _btcpayHost = btcpayHost;
        _httpClient = httpClient ?? new HttpClient();
    }

    public BTCPayServerClient(Uri btcpayHost, string username, string password, HttpClient httpClient = null)
    {
        _apiKey = APIKey;
        _btcpayHost = btcpayHost;
        _username = username;
        _password = password;
        _httpClient = httpClient ?? new HttpClient();
    }

    protected async Task HandleResponse(HttpResponseMessage message)
    {
        if (!message.IsSuccessStatusCode && message.Content?.Headers?.ContentType?.MediaType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) is true)
        {
            if (message.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            {
                var aa = await message.Content.ReadAsStringAsync();
                var err = JsonConvert.DeserializeObject<Models.GreenfieldValidationError[]>(aa);
                throw new GreenfieldValidationException(err);
            }
            if (message.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                var err = JsonConvert.DeserializeObject<Models.GreenfieldPermissionAPIError>(await message.Content.ReadAsStringAsync());
                throw new GreenfieldAPIException((int)message.StatusCode, err);
            }
            else
            {
                var err = JsonConvert.DeserializeObject<Models.GreenfieldAPIError>(await message.Content.ReadAsStringAsync());
                if (err.Code != null)
                    throw new GreenfieldAPIException((int)message.StatusCode, err);
            }
        }
        message.EnsureSuccessStatusCode();
    }

    protected virtual async Task<T> HandleResponse<T>(HttpResponseMessage message)
    {
        await HandleResponse(message);
        var str = await message.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<T>(str);
    }

    public virtual async Task SendHttpRequest(string path,
        Dictionary<string, object> queryPayload = null,
        HttpMethod method = null, CancellationToken cancellationToken = default)
    {
        using var resp = await _httpClient.SendAsync(CreateHttpRequest(path, queryPayload, method), cancellationToken);
        await HandleResponse(resp);
    }

    public virtual async Task<T> SendHttpRequest<T>(string path,
        Dictionary<string, object> queryPayload = null,
        HttpMethod method = null, CancellationToken cancellationToken = default)
    {
        using var resp = await _httpClient.SendAsync(CreateHttpRequest(path, queryPayload, method), cancellationToken);
        return await HandleResponse<T>(resp);
    }

    public virtual async Task SendHttpRequest(string path,
        object bodyPayload = null,
        HttpMethod method = null, CancellationToken cancellationToken = default)
    {
        using var resp = await _httpClient.SendAsync(CreateHttpRequest(path: path, bodyPayload: bodyPayload, method: method), cancellationToken);
        await HandleResponse(resp);
    }

    protected virtual async Task<T> SendHttpRequest<T>(string path,
        object bodyPayload = null,
        HttpMethod method = null, CancellationToken cancellationToken = default)
    {
        using var resp = await _httpClient.SendAsync(CreateHttpRequest(path: path, bodyPayload: bodyPayload, method: method), cancellationToken);
        return await HandleResponse<T>(resp);
    }
        
    protected virtual async Task<TRes> SendHttpRequest<TReq, TRes>(string path,
        Dictionary<string, object> queryPayload = null,
        TReq bodyPayload = default, HttpMethod method = null, CancellationToken cancellationToken = default)
    {
        using var resp = await _httpClient.SendAsync(CreateHttpRequest(path: path, bodyPayload: bodyPayload, queryPayload: queryPayload, method: method), cancellationToken);
        return await HandleResponse<TRes>(resp);
    }

    protected virtual HttpRequestMessage CreateHttpRequest(string path,
        Dictionary<string, object> queryPayload = null,
        HttpMethod method = null)
    {
        var uriBuilder = new UriBuilder(_btcpayHost);
        uriBuilder.Path += (uriBuilder.Path.EndsWith("/") || path.StartsWith("/") ? "" : "/") + path;
        if (queryPayload != null && queryPayload.Any())
        {
            AppendPayloadToQuery(uriBuilder, queryPayload);
        }

        var httpRequest = new HttpRequestMessage(method ?? HttpMethod.Get, uriBuilder.Uri);
        if (_apiKey != null)
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("token", _apiKey);
        else if (!string.IsNullOrEmpty(_username))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(_username + ":" + _password)));
        }

        return httpRequest;
    }

    protected virtual HttpRequestMessage CreateHttpRequest<T>(string path,
        Dictionary<string, object> queryPayload = null,
        T bodyPayload = default, HttpMethod method = null)
    {
        var request = CreateHttpRequest(path, queryPayload, method);
        if (typeof(T).IsPrimitive || !EqualityComparer<T>.Default.Equals(bodyPayload, default(T)))
        {
            request.Content = new StringContent(JsonConvert.SerializeObject(bodyPayload), Encoding.UTF8, "application/json");
        }

        return request;
    }

    protected virtual async Task<T> UploadFileRequest<T>(string apiPath, string filePath, string mimeType, string formFieldName, HttpMethod method = null, CancellationToken token = default)
    {
        using MultipartFormDataContent multipartContent = new();
        using var fileContent = new StreamContent(File.OpenRead(filePath));
        var fileName = Path.GetFileName(filePath);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);
        multipartContent.Add(fileContent, formFieldName, fileName);
        var req = CreateHttpRequest(apiPath, null, method ?? HttpMethod.Post);
        req.Content = multipartContent;
        using var resp = await _httpClient.SendAsync(req, token);
        return await HandleResponse<T>(resp);
    }

    public static void AppendPayloadToQuery(UriBuilder uri, KeyValuePair<string, object> keyValuePair)
    {
        if (uri.Query.Length > 1)
            uri.Query += "&";

        UriBuilder uriBuilder = uri;
        if (!(keyValuePair.Value is string) &&
            keyValuePair.Value.GetType().GetInterfaces().Contains((typeof(IEnumerable))))
        {
            foreach (var item in (IEnumerable)keyValuePair.Value)
            {
                uriBuilder.Query = uriBuilder.Query + Uri.EscapeDataString(keyValuePair.Key) + "=" +
                                   Uri.EscapeDataString(item.ToString()) + "&";
            }
        }
        else
        {
            uriBuilder.Query = uriBuilder.Query + Uri.EscapeDataString(keyValuePair.Key) + "=" +
                               Uri.EscapeDataString(keyValuePair.Value.ToString()) + "&";
        }
        uri.Query = uri.Query.Trim('&');
    }

    public static void AppendPayloadToQuery(UriBuilder uri, Dictionary<string, object> payload)
    {
        if (uri.Query.Length > 1)
            uri.Query += "&";
        foreach (KeyValuePair<string, object> keyValuePair in payload)
        {
            AppendPayloadToQuery(uri, keyValuePair);
        }
    }
}
