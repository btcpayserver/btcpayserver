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
    }
}
