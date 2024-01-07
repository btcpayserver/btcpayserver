using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Bson;

namespace BTCPayServer
{
    public class SelectedChains
    {
        HashSet<string> chains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool all = false;
        public SelectedChains(IConfiguration configuration, Logs logs)
        {
            foreach (var chain in (configuration["chains"] ?? "btc")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.ToUpperInvariant()))
            {
                if (new[] { "ETH", "USDT20", "FAU" }.Contains(chain, StringComparer.OrdinalIgnoreCase))
                {
                    logs.Configuration.LogWarning($"'{chain}' is not anymore supported, please remove it from 'chains'");
                    continue;
                }
                if (chain == "*")
                {
                    all = true;
                    continue;
                }
                chains.Add(chain);
            }
            if (chains.Count == 0)
                chains.Add("BTC");
            if (all)
                chains.Clear();
        }

        public bool Contains(string cryptoCode)
        {
            return all || chains.Contains(cryptoCode);
        }
        public void Add(string cryptoCode)
        {
            chains.Add(cryptoCode);
        }
        public IEnumerable<string> ExplicitlySelected => chains;
    }
}
