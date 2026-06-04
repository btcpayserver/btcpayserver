#nullable enable
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Multisig.Models;
using BTCPayServer.Plugins.Wallets;
using Microsoft.AspNetCore.Authorization;

namespace BTCPayServer.Plugins.Multisig;

public static class Extensions
{
    public static async Task<MultisigSetupAccess> GetSetupAccess(this IAuthorizationService authorizationService, string storeId, ClaimsPrincipal user, MultisigSetupData? pending)
        => new(
            (await authorizationService.AuthorizeAsync(user, storeId, WalletPolicies.CanManageWalletSettings)).Succeeded,
            (await authorizationService.AuthorizeAsync(user, storeId, WalletPolicies.CanSignWalletTransactions)).Succeeded,
            pending?.IsPendingParticipant(user.GetId()) is true);
}
public readonly record struct MultisigSetupAccess(bool CanManageWalletSettings, bool CanSignWalletTransactions, bool IsParticipant)
{
    public bool CanViewStatus => CanManageWalletSettings || (CanSignWalletTransactions && IsParticipant);
}
