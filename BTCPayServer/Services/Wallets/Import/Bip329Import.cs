#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BTCPayServer.Data;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Wallets.Import
{
    // https://github.com/bitcoin/bips/blob/master/bip-0329.mediawiki
    public static class Bip329Import
    {
        public record ImportedLabel(string ObjectType, string ObjectId, string Label);
        public record Result(List<ImportedLabel> Labels, int SkippedLines);

        public static async Task<Result> Parse(TextReader reader, Network network)
        {
            var labels = new List<ImportedLabel>();
            var seen = new HashSet<ImportedLabel>();
            var skipped = 0;
            while (await reader.ReadLineAsync() is { } line)
            {
                line = line.Trim();
                if (line.Length == 0)
                    continue;
                var entry = TryParseLine(line, network);
                if (entry is null)
                    skipped++;
                else if (seen.Add(entry))
                    labels.Add(entry);
            }
            return new Result(labels, skipped);
        }

        static ImportedLabel? TryParseLine(string line, Network network)
        {
            JObject obj;
            try
            {
                obj = JObject.Parse(line);
            }
            catch (JsonException)
            {
                return null;
            }
            var label = GetString(obj, "label")?.Trim();
            var reference = GetString(obj, "ref");
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(reference))
                return null;
            return GetString(obj, "type") switch
            {
                "tx" when uint256.TryParse(reference, out var txId) =>
                    new ImportedLabel(WalletObjectData.Types.Tx, txId.ToString(), label),
                "addr" when TryParseAddress(reference, network) is { } address =>
                    new ImportedLabel(WalletObjectData.Types.Address, address, label),
                "output" when TryParseOutpoint(reference) is { } outpoint =>
                    new ImportedLabel(WalletObjectData.Types.Utxo, outpoint, label),
                _ => null
            };
        }

        static string? GetString(JObject obj, string name)
            => obj[name] is JValue { Type: JTokenType.String, Value: string s } ? s : null;

        static string? TryParseAddress(string str, Network network)
        {
            try
            {
                return BitcoinAddress.Create(str, network).ToString();
            }
            catch (FormatException)
            {
                return null;
            }
        }

        static string? TryParseOutpoint(string str)
        {
            // BIP-329 references outputs as <txid>:<vout>, NBitcoin parses <txid>-<vout>
            return OutPoint.TryParse(str.Replace(':', '-'), out var outpoint) ? outpoint!.ToString() : null;
        }
    }
}
