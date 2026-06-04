using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions;
using BTCPayServer.JsonConverters;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Multisig.Models;

public class MultisigSetupData
{
    public string RequestId { get; set; }
    public string StoreId { get; set; }
    public string CryptoCode { get; set; }
    public string RequestedByUserId { get; set; }
    public string ScriptType { get; set; }
    public int RequiredSigners { get; set; }
    public int TotalSigners { get; set; }
    public bool ReplacesExistingWallet { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    [JsonIgnore]
    public List<PendingMultisigSetupParticipantData> Participants { get; set; } = new();
    [JsonConverter(typeof(RequestBaseUrlConverter))]
    public RequestBaseUrl RequestBaseUrl { get; set; }
    public bool IsPendingParticipant(string userId)
    => !string.IsNullOrEmpty(userId) &&
       Participants.Any(p => string.Equals(p.UserId, userId, StringComparison.Ordinal));

    public DerivationSchemeSettings GetDiredivationSchemeSettings(BTCPayNetwork network)
    {
        var suffix = ScriptType.ToLowerInvariant() switch
        {
            "p2wsh" => string.Empty,
            "p2sh-p2wsh" => "-[p2sh]",
            "p2sh" => "-[legacy]",
            _ => string.Empty
        };

        var multisigDerivation = $"{RequiredSigners}-of-{string.Join("-", Participants.Select(k => k.AccountKey.ToString()))}{suffix}";
        var strategy = new DerivationSchemeSettings(new DerivationSchemeParser(network).Parse(multisigDerivation), network);
        strategy.Source = "ManualDerivationScheme";
        strategy.IsMultiSigOnServer = true;
        strategy.DefaultIncludeNonWitnessUtxo = true;
        for (int i = 0; i < Participants.Count; i++)
        {
            strategy.AccountKeySettings[i].SignerUserId = Participants[i].UserId;
            strategy.AccountKeySettings[i].AccountKeyPath =  Participants[i].AccountKeyPath.KeyPath;
            strategy.AccountKeySettings[i].RootFingerprint =  Participants[i].AccountKeyPath.MasterFingerprint;
        }
        return strategy;
    }
}

public class PendingMultisigSetupParticipantData
{
    public string UserId { get; set; }
    public string Email { get; set; }
    public string Name { get; set; }
    public string AccountKey { get; set; }
    public RootedKeyPath AccountKeyPath { get; set; }
}
