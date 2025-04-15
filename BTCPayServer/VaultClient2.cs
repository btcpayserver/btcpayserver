#nullable  enable
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace BTCPayServer;

public class VaultClient2
{
    private readonly IJSRuntime _js;

    public VaultClient2(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<VaultPermissionResult> AskPermission(CancellationToken cancellationToken)
    {
        return await this._js.InvokeAsync<VaultPermissionResult>("vault.askVaultPermission", cancellationToken);    
    }

    public async Task<string> SendHwi(string[] arguments, CancellationToken cancellationToken)
    {
        var obj = new JsonObject()
        {
            ["params"] = new JsonArray(arguments.Select(a => JsonValue.Create(a)).ToArray<JsonNode?>())
        };
        var resp = await this._js.InvokeAsync<HwiResponse>("vault.sendHwi", cancellationToken, obj);
        return resp.Body.GetValue<string>();
    }

    public class HwiResponse
    {
        public int HttpCode { get; set; }
        public string? Error { get; set; }
        public JsonNode? Body { get; set; }
    }

    public async Task SetXPub(SetXPubRequest o, CancellationToken cancellationToken)
    {
        await this._js.InvokeVoidAsync("vault.setXPub", cancellationToken, o);
    }
}

public class SetXPubRequest
{
    public string? Strategy { get; set; }
    public string? AccountKey { get; set; }
    public string? KeyPath { get; set; }
    public string? Fingerprint { get; set; }
}

public class VaultPermissionResult
{
    public int HttpCode { get; set; }
    public string? Browser { get; set; }
}
