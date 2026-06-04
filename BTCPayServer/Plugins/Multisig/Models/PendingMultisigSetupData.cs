using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

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
    [JsonConverter(typeof(RequestBaseUrlConverter))]
    public RequestBaseUrl RequestBaseUrl { get; set; }
    public bool IsPendingParticipant(string userId)
    => !string.IsNullOrEmpty(userId) &&
       Participants.Any(p => string.Equals(p.UserId, userId, StringComparison.Ordinal));
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
