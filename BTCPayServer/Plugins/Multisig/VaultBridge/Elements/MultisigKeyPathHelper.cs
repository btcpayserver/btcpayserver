using System;
using NBitcoin;

namespace BTCPayServer.Plugins.Multisig.VaultBridge.Elements;

public static class MultisigKeyPathHelper
{
    public static KeyPath BuildDefaultPath(KeyPath coinType, string scriptType, int accountNumber)
    {
        accountNumber = Math.Max(0, accountNumber);
        var normalized = scriptType?.ToLowerInvariant();
        return normalized switch
        {
            "p2sh-p2wsh" => new KeyPath("48'").Derive(coinType).Derive(accountNumber, true).Derive(1, true),
            "p2sh" => new KeyPath("45'").Derive(coinType).Derive(accountNumber, true),
            "p2wsh" => new KeyPath("48'").Derive(coinType).Derive(accountNumber, true).Derive(2, true),
            _ => throw new ArgumentOutOfRangeException(nameof(scriptType), scriptType, "Unsupported multisig script type.")
        };
    }
}
