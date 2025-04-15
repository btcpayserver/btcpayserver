using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer;

public class VaultHWITransport2 : Hwi.Transports.ITransport
{
    private readonly VaultClient2 _client;

    public VaultHWITransport2(VaultClient2 client)
    {
        _client = client;
    }
    public async Task<string> SendCommandAsync(string[] arguments, CancellationToken cancellationToken)
    {
        return await _client.SendHwi(arguments, cancellationToken);
    }
}
