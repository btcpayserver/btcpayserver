using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer;
using NBXplorer.DerivationStrategy;

namespace BTCPayServer
{
    public class DerivationSchemeParser
    {
        public Network Network { get; set; }
        public Script HintScriptPubKey { get; set; }

        public DerivationSchemeParser(Network expectedNetwork)
        {
            Network = expectedNetwork;
        }

        public DerivationStrategyBase Parse(string str)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));
            str = str.Trim();

            HashSet<string> hintedLabels = new HashSet<string>();

            var hintDestination = HintScriptPubKey?.GetDestination();
            if (hintDestination != null)
            {
                if (hintDestination is KeyId)
                {
                    hintedLabels.Add("legacy");
                }
                if (hintDestination is ScriptId)
                {
                    hintedLabels.Add("p2sh");
                }
            }

            if(!Network.Consensus.SupportSegwit)
                hintedLabels.Add("legacy");

            try
            {
                var result = new DerivationStrategyFactory(Network).Parse(str);
                return FindMatch(hintedLabels, result);
            }
            catch
            {
            }

            Dictionary<uint, string[]> electrumMapping = new Dictionary<uint, string[]>();
            //Source https://github.com/spesmilo/electrum/blob/9edffd17542de5773e7284a8c8a2673c766bb3c3/lib/bitcoin.py
            var standard = 0x0488b21eU;
            electrumMapping.Add(standard, new[] { "legacy" });
            var p2wpkh_p2sh = 0x049d7cb2U;
            electrumMapping.Add(p2wpkh_p2sh, new string[] { "p2sh" });
            var p2wpkh = 0x4b24746U;
            electrumMapping.Add(p2wpkh, Array.Empty<string>());

            var parts = str.Split('-');
            bool hasLabel = false;
            for (int i = 0; i < parts.Length; i++)
            {
                if (IsLabel(parts[i]))
                {
                    if (!hasLabel)
                    {
                        hintedLabels.Clear();
                        if (!Network.Consensus.SupportSegwit)
                            hintedLabels.Add("legacy");
                    }
                    hasLabel = true;
                    hintedLabels.Add(parts[i].Substring(1, parts[i].Length - 2).ToLowerInvariant());
                    continue;
                }
                try
                {
                    var data = Network.GetBase58CheckEncoder().DecodeData(parts[i]);
                    if (data.Length < 4)
                        continue;
                    var prefix = Utils.ToUInt32(data, false);
                    var standardPrefix = Utils.ToBytes(Network.NetworkType == NetworkType.Mainnet ? 0x0488b21eU : 0x043587cf, false);
                    for (int ii = 0; ii < 4; ii++)
                        data[ii] = standardPrefix[ii];

                    var derivationScheme = new BitcoinExtPubKey(Network.GetBase58CheckEncoder().EncodeData(data), Network).ToString();
                    electrumMapping.TryGetValue(prefix, out string[] labels);
                    if (labels != null)
                    {
                        foreach (var label in labels)
                        {
                            hintedLabels.Add(label.ToLowerInvariant());
                        }
                    }
                    parts[i] = derivationScheme;
                }
                catch { continue; }
            }

            if (hintDestination != null)
            {
                if (hintDestination is WitKeyId)
                {
                    hintedLabels.Remove("legacy");
                    hintedLabels.Remove("p2sh");
                }
            }

            str = string.Join('-', parts.Where(p => !IsLabel(p)));
            foreach (var label in hintedLabels)
            {
                str = $"{str}-[{label}]";
            }

            return FindMatch(hintedLabels, new DerivationStrategyFactory(Network).Parse(str));
        }

        private DerivationStrategyBase FindMatch(HashSet<string> hintLabels, DerivationStrategyBase result)
        {
            var facto = new DerivationStrategyFactory(Network);
            var firstKeyPath = new KeyPath("0/0");
            if (HintScriptPubKey == null)
                return result;
            if (HintScriptPubKey == result.Derive(firstKeyPath).ScriptPubKey)
                return result;

            if (result is MultisigDerivationStrategy)
                hintLabels.Add("keeporder");

            var resultNoLabels = result.ToString();
            resultNoLabels = string.Join('-', resultNoLabels.Split('-').Where(p => !IsLabel(p)));
            foreach (var labels in ItemCombinations(hintLabels.ToList()))
            {
                var hinted = facto.Parse(resultNoLabels + '-' + string.Join('-', labels.Select(l=>$"[{l}]").ToArray()));
                if (HintScriptPubKey == hinted.Derive(firstKeyPath).ScriptPubKey)
                    return hinted;
            }
            throw new FormatException("Could not find any match");
        }

        private static bool IsLabel(string v)
        {
            return v.StartsWith('[') && v.EndsWith(']');
        }

        /// <summary>
		/// Method to create lists containing possible combinations of an input list of items. This is 
		/// basically copied from code by user "jaolho" on this thread:
		/// http://stackoverflow.com/questions/7802822/all-possible-combinations-of-a-list-of-values
		/// </summary>
		/// <typeparam name="T">type of the items on the input list</typeparam>
		/// <param name="inputList">list of items</param>
		/// <param name="minimumItems">minimum number of items wanted in the generated combinations, 
		///                            if zero the empty combination is included,
		///                            default is one</param>
		/// <param name="maximumItems">maximum number of items wanted in the generated combinations,
		///                            default is no maximum limit</param>
		/// <returns>list of lists for possible combinations of the input items</returns>
		public static List<List<T>> ItemCombinations<T>(List<T> inputList, int minimumItems = 1,
                                                        int maximumItems = int.MaxValue)
        {
            int nonEmptyCombinations = (int)Math.Pow(2, inputList.Count) - 1;
            List<List<T>> listOfLists = new List<List<T>>(nonEmptyCombinations + 1);

            if (minimumItems == 0)  // Optimize default case
                listOfLists.Add(new List<T>());

            for (int i = 1; i <= nonEmptyCombinations; i++)
            {
                List<T> thisCombination = new List<T>(inputList.Count);
                for (int j = 0; j < inputList.Count; j++)
                {
                    if ((i >> j & 1) == 1)
                        thisCombination.Add(inputList[j]);
                }

                if (thisCombination.Count >= minimumItems && thisCombination.Count <= maximumItems)
                    listOfLists.Add(thisCombination);
            }

            return listOfLists;
        }
    }
}
