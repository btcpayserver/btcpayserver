using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BTCPayServer
{
    public class VaultHWITransport : Hwi.Transports.ITransport
    {
        private readonly VaultClient _vaultClient;

        public VaultHWITransport(VaultClient vaultClient)
        {
            _vaultClient = vaultClient;
        }
        public async Task<string> SendCommandAsync(string[] arguments, CancellationToken cancel)
        {
            var resp = await _vaultClient.SendVaultRequest("http://127.0.0.1:65092/hwi-bridge/v1",
                new JObject()
                {
                    ["params"] = new JArray(arguments)
                }, cancel);
            return (string)((JValue)resp).Value;
        }
    }
}
