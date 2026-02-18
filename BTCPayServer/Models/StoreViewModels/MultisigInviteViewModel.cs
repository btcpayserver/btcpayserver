using System;

namespace BTCPayServer.Models.StoreViewModels;

public class MultisigInviteViewModel
{
    public string StoreId { get; set; }
    public string CryptoCode { get; set; }
    public string Token { get; set; }
    public string RequestId { get; set; }
    public string UserId { get; set; }
    public string UserEmail { get; set; }
    public string UserName { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int RequiredSigners { get; set; }
    public int TotalSigners { get; set; }
    public string ScriptType { get; set; }
    public string AccountKey { get; set; }
    public string MasterFingerprint { get; set; }
    public string AccountKeyPath { get; set; }
    public bool Submitted { get; set; }
}
