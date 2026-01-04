using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BTCPayServer;

public class VaultHWITransport : Hwi.Transports.ITransport
{
    private readonly VaultClient _client;

    public VaultHWITransport(VaultClient client)
    {
        _client = client;
    }
    public async Task<string> SendCommandAsync(string[] arguments, CancellationToken cancellationToken)
    {
        return (await _client.SendVaultRequest(null, new JObject()
        {
            ["params"] = new JArray(arguments)
        }, cancellationToken)).Value<string>() ?? "";
    }
}
