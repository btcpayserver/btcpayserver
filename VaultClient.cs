#nullable  enable
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Newtonsoft.Json.Linq;

namespace BTCPayServer;

public class VaultClient(IJSRuntime js, string serviceUri)
{
    public class VaultNotConnectedException() : Exception("Vault not connected");

    public class VaultException(string message) : Exception(message);

    public async Task<VaultPermissionResult> AskPermission(CancellationToken cancellationToken)
    {
        return await js.InvokeAsync<VaultPermissionResult>("vault.askVaultPermission", cancellationToken, serviceUri);    
    }
    public async Task<JToken?> SendVaultRequest(string? path, JObject? body, CancellationToken cancellationToken)
    {
        var isAbsolute = path is not null && Uri.IsWellFormedUriString(path, UriKind.Absolute);
        var query = new JsonObject()
        {
            ["uri"] = isAbsolute ? path : serviceUri + path
        };
        if (body is not null)
            query["body"] = JsonObject.Parse(body.ToString());
        var resp = await js.InvokeAsync<SendRequestResponse>("vault.sendRequest", cancellationToken, query);
        if (resp.HttpCode is not { } p)
            throw new VaultNotConnectedException();
        if (p != 200)
            throw new VaultException($"Unexpected response code from vault {p}");
        return (resp.Body)?.ToJsonString() is { } str ? JToken.Parse(str) : null;
    }

    public class SendRequestResponse
    {
        public int? HttpCode { get; set; }
        public JsonNode? Body { get; set; }
    }
    public class HwiResponse
    {
        public int HttpCode { get; set; }
        public string? Error { get; set; }
        public JsonNode? Body { get; set; }
    }
}

public class VaultPermissionResult
{
    public int HttpCode { get; set; }
    public string? Browser { get; set; }
}
