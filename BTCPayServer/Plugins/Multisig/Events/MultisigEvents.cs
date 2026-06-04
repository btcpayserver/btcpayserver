#nullable enable

using System.Collections.Generic;
using BTCPayServer.Plugins.Multisig.Models;

namespace BTCPayServer.Plugins.Multisig.Events;

public record MultisigSignerInfo(string Email, string Name);
public record MultisigSignerKeyRequestedEvent(
    MultisigSetupData Setup,
    MultisigSignerInfo Signer)
{
    public override string ToString() => nameof(MultisigSignerKeyRequestedEvent);
}

public record MultisigSignerKeySubmittedEvent(
    MultisigSetupData Setup,
    MultisigSignerInfo Signer)
{
    public override string ToString() => nameof(MultisigSignerKeySubmittedEvent);
}

public record MultisigWalletCreatedEvent(
    MultisigSetupData Setup,
    string WalletLink,
    IReadOnlyCollection<string> ParticipantUserIds)
{
    public override string ToString() => nameof(MultisigWalletCreatedEvent);
}
