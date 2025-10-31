using System;
using System.Collections.Generic;
using BTCPayServer.Lightning;
using NBitcoin;

namespace BTCPayServer.Lightning.LNbits
{
        private readonly HttpClient _httpClient;

        public LNbitsConnectionStringHandler(HttpClient httpClient = null)
        {
            _httpClient = httpClient;
        }
    public class LNbitsConnectionStringHandler : ILightningConnectionStringHandler
    {
        public ILightningClient Create(string connectionString, Network network, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                error = "Connection string cannot be empty";
                return null;
            }

            try
            {
                var parts = ParseConnectionString(connectionString);

                if (!parts.TryGetValue("type", out var type) || type != "lnbits")
                {
                    error = "Connection type must be 'lnbits'";
                    return null;
                }

                if (!parts.TryGetValue("server", out var server))
                {
                    error = "Missing required parameter: 'server'";
                    return null;
                }

                if (!parts.TryGetValue("wallet-id", out var walletId))
                {
                    error = "Missing required parameter: 'wallet-id'";
                    return null;
                }

                if (!parts.TryGetValue("api-key", out var apiKey))
                {
                    error = "Missing required parameter: 'api-key'";
                    return null;
                }

                if (!Uri.TryCreate(server, UriKind.Absolute, out var serverUri))
                {
                    error = $"Invalid server URL: {server}";
                    return null;
                }

                return new LNbitsLightningClient(serverUri, walletId, apiKey);
            }
            catch (Exception ex)
            {
                error = $"Failed to parse LNbits connection string: {ex.Message}";
                return null;
            }
        }

        private Dictionary<string, string> ParseConnectionString(string connectionString)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                var keyValue = part.Split('=', 2);
                if (keyValue.Length == 2)
                {
                    var key = keyValue[0].Trim();
                    var value = keyValue[1].Trim();
                    result[key] = value;
                }
            }

            return result;
        }
    }
}
