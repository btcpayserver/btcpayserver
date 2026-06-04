#nullable enable

using System.Collections.Generic;
using BTCPayServer.Plugins.Multisig.Models;

namespace BTCPayServer.Plugins.Multisig.Events;

public record MultisigSignerInfo(string Email, string Name);
public record MultisigSignerKeyRequestedEvent(
    PendingMultisigSetupData Setup,
    MultisigSignerInfo Signer)
{
    public override string ToString() => nameof(MultisigSignerKeyRequestedEvent);
}

public record MultisigSignerKeySubmittedEvent(
    PendingMultisigSetupData Setup,
    MultisigSignerInfo Signer)
{
    public override string ToString() => nameof(MultisigSignerKeySubmittedEvent);
}

public record MultisigWalletCreatedEvent(
    PendingMultisigSetupData Setup,
    string WalletLink,
    IReadOnlyCollection<string> ParticipantUserIds)
{
    public override string ToString() => nameof(MultisigWalletCreatedEvent);
}
