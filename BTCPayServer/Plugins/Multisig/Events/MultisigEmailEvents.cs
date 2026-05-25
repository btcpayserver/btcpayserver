#nullable enable

using System.Collections.Generic;

namespace BTCPayServer.Plugins.Multisig.Events;

public record MultisigSignerKeyRequestedEvent(
    string StoreId,
    string CryptoCode,
    string RequestId,
    string SignerUserId,
    string? SignerEmail,
    string? SignerName,
    string SignerLink);

public record MultisigSignerKeySubmittedEvent(
    string StoreId,
    string CryptoCode,
    string RequestId,
    string? RequestedByEmail,
    string? SignerUserId,
    string? SignerEmail,
    string? SignerName,
    string SetupLink);

public record MultisigWalletCreatedEvent(
    string StoreId,
    string CryptoCode,
    string RequestId,
    string WalletLink,
    IReadOnlyCollection<string> ParticipantUserIds);
