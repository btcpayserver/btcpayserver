using System;
using System.Collections.Generic;

namespace BTCPayServer.Models.StoreViewModels;

public class PendingMultisigSetupData
{
    public string RequestId { get; set; }
    public string CryptoCode { get; set; }
    public string RequestedByUserId { get; set; }
    public string RequestedByEmail { get; set; }
    public string RequestedByName { get; set; }
    public string ScriptType { get; set; }
    public int RequiredSigners { get; set; }
    public int TotalSigners { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Finalized { get; set; }
    public List<PendingMultisigSetupParticipantData> Participants { get; set; } = new();
}

public class PendingMultisigSetupParticipantData
{
    public string UserId { get; set; }
    public string Email { get; set; }
    public string Name { get; set; }
    public string AccountKey { get; set; }
    public string MasterFingerprint { get; set; }
    public string AccountKeyPath { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
}
