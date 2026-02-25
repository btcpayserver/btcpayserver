using System;

namespace BTCPayServer.Models;

public class MultisigInProgressViewModel
{
    public string StoreId { get; set; }
    public string StoreName { get; set; }
    public string CryptoCode { get; set; }
    public string RequestId { get; set; }
    public string ScriptType { get; set; }
    public int RequiredSigners { get; set; }
    public int TotalSigners { get; set; }
    public int SubmittedSigners { get; set; }
    public bool YourKeySubmitted { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string InviteUrl { get; set; }
}
