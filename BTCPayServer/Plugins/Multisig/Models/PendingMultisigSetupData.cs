using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.Multisig.Models;

public class PendingMultisigSetupData
{
    public string RequestId { get; set; }
    public string StoreId { get; set; }
    public string CryptoCode { get; set; }
    public string RequestedByEmail { get; set; }
    public string ScriptType { get; set; }
    public int RequiredSigners { get; set; }
    public int TotalSigners { get; set; }
    public bool ReplacesExistingWallet { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public List<PendingMultisigSetupParticipantData> Participants { get; set; } = new();
}

public sealed record PendingMultisigSetupContext(
    string StoreId,
    string CryptoCode,
    string SettingName,
    PendingMultisigSetupData Pending,
    uint XMin);

public class PendingMultisigSetupParticipantData
{
    public string UserId { get; set; }
    public string Email { get; set; }
    public string Name { get; set; }
    public string AccountKey { get; set; }
    public string MasterFingerprint { get; set; }
    public string AccountKeyPath { get; set; }
}
