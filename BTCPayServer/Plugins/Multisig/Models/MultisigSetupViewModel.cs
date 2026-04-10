using System;
using System.Collections.Generic;
using BTCPayServer.Models.StoreViewModels;

namespace BTCPayServer.Plugins.Multisig.Models;

public class MultisigSetupViewModel : DerivationSchemeViewModel
{
    public string StoreId { get; set; }
    public int? MultisigRequiredSigners { get; set; }
    public int? MultisigTotalSigners { get; set; }
    public string MultisigScriptType { get; set; }
    public string[] MultisigSigners { get; set; }
    public string[] MultisigSignerFingerprints { get; set; }
    public string[] MultisigSignerKeyPaths { get; set; }
    public string[] MultisigParticipantUserIds { get; set; }
    public string MultisigRequestId { get; set; }
    public string MultisigRemoveUserId { get; set; }
    public PendingMultisigSetupData MultisigPendingSetup { get; set; }
    public Dictionary<string, string> MultisigInviteLinks { get; set; } = new(StringComparer.Ordinal);
    public List<MultisigStoreUserItem> MultisigStoreUsers { get; set; } = new();
}

public class MultisigStoreUserItem
{
    public string UserId { get; set; }
    public string Email { get; set; }
    public string Name { get; set; }
    public bool Selected { get; set; }
}
