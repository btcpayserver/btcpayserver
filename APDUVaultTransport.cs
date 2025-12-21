using Newtonsoft.Json.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;
using System.Threading;
using BTCPayServer.NTag424;
using NBitcoin.DataEncoders;
using System;
using SocketIOClient;

namespace BTCPayServer
{
    public class APDUVaultTransport : IAPDUTransport
    {
        private readonly VaultClient _vaultClient;

        public APDUVaultTransport(VaultClient vaultClient)
        {
            _vaultClient = vaultClient;
        }


        public async Task WaitForCard(CancellationToken cancellationToken)
        {
            await _vaultClient.SendVaultRequest("/wait-for-card", null, cancellationToken);
        }
        public async Task WaitForRemoved(CancellationToken cancellationToken)
        {
            await _vaultClient.SendVaultRequest("/wait-for-disconnected", null, cancellationToken);
        }

        public async Task<NtagResponse> SendAPDU(byte[] apdu, CancellationToken cancellationToken)
        {
            var resp = await _vaultClient.SendVaultRequest("/",
                new JObject()
                {
                    ["apdu"] = Encoders.Hex.EncodeData(apdu)
                }, cancellationToken);
            var data = Encoders.Hex.DecodeData(resp["data"].Value<string>());
            return new NtagResponse(data, resp["status"]!.Value<ushort>());
        }
    }
}
