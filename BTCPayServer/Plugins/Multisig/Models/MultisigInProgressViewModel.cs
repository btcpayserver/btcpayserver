using System;

namespace BTCPayServer.Plugins.Multisig.Models;

public class MultisigInProgressViewModel
{
    public string StoreId { get; set; }
    public string CryptoCode { get; set; }
    public string RequestId { get; set; }
    public int RequiredSigners { get; set; }
    public int TotalSigners { get; set; }
    public int SubmittedSigners { get; set; }
    public bool DidParticipate { get; set; }
    public bool YourKeySubmitted { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string SessionUrl { get; set; }
    public bool CanCreateWallet { get; set; }
    public MultisigInProgressParticipantViewModel[] Participants { get; set; } = [];

    public int MissingSigners => Math.Max(0, TotalSigners - SubmittedSigners);
    public bool CanSubmitSignerKey => DidParticipate && !YourKeySubmitted;
    public bool ReadyToCreateWallet => MissingSigners == 0;
}

public class MultisigInProgressParticipantViewModel
{
    public string Email { get; set; }
    public string Name { get; set; }
    public bool Submitted { get; set; }
}
