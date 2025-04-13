using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using NBitcoin;
using NBitcoin.Scripting;
using NBXplorer.DerivationStrategy;

namespace BTCPayServer
{
    public class DerivationSchemeParser
    {
        public BTCPayNetwork BtcPayNetwork { get; }

        public Network Network => BtcPayNetwork.NBitcoinNetwork;

        public DerivationSchemeParser(BTCPayNetwork expectedNetwork)
        {
            ArgumentNullException.ThrowIfNull(expectedNetwork);
            BtcPayNetwork = expectedNetwork;
        }

        public (DerivationStrategyBase, RootedKeyPath[]) ParseOutputDescriptor(string str)
        {
            (DerivationStrategyBase, RootedKeyPath[]) ExtractFromPkProvider(PubKeyProvider pubKeyProvider,
                string suffix = "")
            {
                switch (pubKeyProvider)
                {
                    case PubKeyProvider.Const _:
                        throw new FormatException("Only HD output descriptors are supported.");
                    case PubKeyProvider.HD hd:
                        if (hd.Path != null && hd.Path.ToString() != "0")
                        {
                            throw new FormatException("Custom change paths are not supported.");
                        }
                        return (Parse($"{hd.Extkey}{suffix}"), null);
                    case PubKeyProvider.Origin origin:
                        var innerResult = ExtractFromPkProvider(origin.Inner, suffix);
                        return (innerResult.Item1, new[] { origin.KeyOriginInfo });
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            (DerivationStrategyBase, RootedKeyPath[]) ExtractFromMulti(OutputDescriptor.Multi multi)
            {
                var xpubs = multi.PkProviders.Select(provider => ExtractFromPkProvider(provider));
                return (
                    Parse(
                        $"{multi.Threshold}-of-{(string.Join('-', xpubs.Select(tuple => tuple.Item1.ToString())))}{(multi.IsSorted ? "" : "-[keeporder]")}"),
                    xpubs.SelectMany(tuple => tuple.Item2).ToArray());
            }

            ArgumentNullException.ThrowIfNull(str);
            str = str.Trim();
            //nbitcoin output descriptor does not support taproot, so let's check if it is a taproot descriptor and fake until it is supported

            var outputDescriptor = OutputDescriptor.Parse(str, Network);
            switch (outputDescriptor)
            {
                case OutputDescriptor.PK _:
                case OutputDescriptor.Raw _:
                case OutputDescriptor.Addr _:
                    throw new FormatException("Only HD output descriptors are supported.");
                case OutputDescriptor.Combo _:
                    throw new FormatException("Only output descriptors of one format are supported.");
                case OutputDescriptor.Multi multi:
                    return ExtractFromMulti(multi);
                case OutputDescriptor.PKH pkh:
                    return ExtractFromPkProvider(pkh.PkProvider, "-[legacy]");
                case OutputDescriptor.SH sh:
                    var suffix = "-[p2sh]";
                    if (sh.Inner is OutputDescriptor.Multi)
                    {
                        //non segwit
                        suffix = "-[legacy]";
                    }

                    if (sh.Inner is OutputDescriptor.Multi || sh.Inner is OutputDescriptor.WPKH ||
                        sh.Inner is OutputDescriptor.WSH)
                    {
                        var ds = ParseOutputDescriptor(sh.Inner.ToString());
                        return (Parse(ds.Item1 + suffix), ds.Item2);
                    };
                    throw new FormatException("sh descriptors are only supported with multsig(legacy or p2wsh) and segwit(p2wpkh)");
                case OutputDescriptor.Tr tr:
                    return ExtractFromPkProvider(tr.InnerPubkey, "-[taproot]");
                case OutputDescriptor.WPKH wpkh:
                    return ExtractFromPkProvider(wpkh.PkProvider);
                case OutputDescriptor.WSH { Inner: OutputDescriptor.Multi multi }:
                    return ExtractFromMulti(multi);
                case OutputDescriptor.WSH:
                    throw new FormatException("wsh descriptors are only supported with multisig");
                default:
                    throw new ArgumentOutOfRangeException(nameof(outputDescriptor));
            }
        }
        public DerivationStrategyBase Parse(string str)
        {
            ArgumentNullException.ThrowIfNull(str);
            str = str.Trim();
            str = Regex.Replace(str, @"\s+", "");
            HashSet<string> hintedLabels = new HashSet<string>();
            if (!Network.Consensus.SupportSegwit)
            {
                hintedLabels.Add("legacy");
                str = str.Replace("-[p2sh]", string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            try
            {
                return BtcPayNetwork.NBXplorerNetwork.DerivationStrategyFactory.Parse(str);
            }
            catch
            {
            }

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
                    var standardPrefix = Utils.ToBytes(0x0488b21eU, false);
                    for (int ii = 0; ii < 4; ii++)
                        data[ii] = standardPrefix[ii];

                    var derivationScheme = GetBitcoinExtPubKeyByNetwork(Network, data).ToString();

                    if (BtcPayNetwork.ElectrumMapping.TryGetValue(prefix, out var type))
                    {
                        switch (type)
                        {
                            case DerivationType.Legacy:
                                hintedLabels.Add("legacy");
                                break;
                            case DerivationType.SegwitP2SH:
                                hintedLabels.Add("p2sh");
                                break;
                        }
                    }
                    parts[i] = derivationScheme;
                }
                catch { continue; }
            }

            str = string.Join('-', parts.Where(p => !IsLabel(p)));
            foreach (var label in hintedLabels)
            {
                str = $"{str}-[{label}]";
            }
            return BtcPayNetwork.NBXplorerNetwork.DerivationStrategyFactory.Parse(str);
        }

        internal DerivationStrategyBase ParseElectrum(string str)
        {
            ArgumentNullException.ThrowIfNull(str);
            str = str.Trim();
            var data = Network.GetBase58CheckEncoder().DecodeData(str);
            if (data.Length < 4)
                throw new FormatException();
            var prefix = Utils.ToUInt32(data, false);

            var standardPrefix = Utils.ToBytes(0x0488b21eU, false);
            for (int ii = 0; ii < 4; ii++)
                data[ii] = standardPrefix[ii];
            var extPubKey = GetBitcoinExtPubKeyByNetwork(Network, data);
            if (!BtcPayNetwork.ElectrumMapping.TryGetValue(prefix, out var type))
            {
                throw new FormatException();
            }
            if (type == DerivationType.Segwit)
                return new DirectDerivationStrategy(extPubKey, true);
            if (type == DerivationType.Legacy)
                return new DirectDerivationStrategy(extPubKey, false);
            if (type == DerivationType.SegwitP2SH)
                return BtcPayNetwork.NBXplorerNetwork.DerivationStrategyFactory.Parse(extPubKey.ToString() + "-[p2sh]");
            throw new FormatException();
        }

        public static BitcoinExtPubKey GetBitcoinExtPubKeyByNetwork(Network network, byte[] data)
        {
            try
            {
                return new BitcoinExtPubKey(network.GetBase58CheckEncoder().EncodeData(data), network.NetworkSet.Mainnet).ToNetwork(network);
            }
            catch (Exception)
            {
                return new BitcoinExtPubKey(network.GetBase58CheckEncoder().EncodeData(data), Network.Main).ToNetwork(network);
            }
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
