using System;
using NBitcoin;

namespace BTCPayServer
{
    public static class PSBTExtensions
    {
        public static bool PSBTChanged(this PSBT psbt, Action act)
        {
            var before = psbt.ToBase64();
            act();
            var after = psbt.ToBase64();
            return before != after;
        }

        public static PSBT AddWitnessUtxoToSegwitInputs(this PSBT psbt)
        {
            foreach (var input in psbt.Inputs)
            {
                var nonWitnessUtxo = input.NonWitnessUtxo;
                if (input.WitnessUtxo is not null ||
                    nonWitnessUtxo is null ||
                    nonWitnessUtxo.GetHash() != input.PrevOut.Hash ||
                    input.PrevOut.N >= (uint)nonWitnessUtxo.Outputs.Count)
                    continue;

                var previousOutput = nonWitnessUtxo.Outputs[(int)input.PrevOut.N];
                var redeemScript = input.RedeemScript;
                if (previousOutput.ScriptPubKey.IsScriptType(ScriptType.Witness) ||
                    redeemScript is not null &&
                    redeemScript.IsScriptType(ScriptType.Witness) &&
                    redeemScript.Hash.ScriptPubKey == previousOutput.ScriptPubKey)
                {
                    input.WitnessUtxo = previousOutput;
                }
            }

            return psbt;
        }
    }
}
