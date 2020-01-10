using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Rates
{
    public class CoinAverageSettingsAuthenticator : ICoinAverageAuthenticator
    {
        CoinAverageSettings _Settings;
        public CoinAverageSettingsAuthenticator(CoinAverageSettings settings)
        {
            _Settings = settings;
        }
        public Task AddHeader(HttpRequestMessage message)
        {
            return _Settings.AddHeader(message);
        }
    }
    public class CoinAverageSettings : ICoinAverageAuthenticator
    {
        private static readonly DateTime _epochUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public (String PublicKey, String PrivateKey)? KeyPair { get; set; }
        
        public Task AddHeader(HttpRequestMessage message)
        {
            var signature = GetCoinAverageSignature();
            if (signature != null)
            {
                message.Headers.Add("X-signature", signature);
            }
            return Task.CompletedTask;
        }

        public string GetCoinAverageSignature()
        {
            var keyPair = KeyPair;
            if (!keyPair.HasValue)
                return null;
            if (string.IsNullOrEmpty(keyPair.Value.PublicKey) || string.IsNullOrEmpty(keyPair.Value.PrivateKey))
                return null;
            var timestamp = (int)((DateTime.UtcNow - _epochUtc).TotalSeconds);
            var payload = timestamp + "." + keyPair.Value.PublicKey;
            var digestValueBytes = new HMACSHA256(Encoding.ASCII.GetBytes(keyPair.Value.PrivateKey)).ComputeHash(Encoding.ASCII.GetBytes(payload));
            var digestValueHex = NBitcoin.DataEncoders.Encoders.Hex.EncodeData(digestValueBytes);
            return payload + "." + digestValueHex;
        }
    }
}
