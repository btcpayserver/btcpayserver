#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Multisig.Models;
using BTCPayServer.Plugins.Wallets;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Multisig.Services;

public class MultisigService(
    StoreRepository storeRepository,
    ApplicationDbContextFactory dbContextFactory,
    PaymentMethodHandlerDictionary handlers,
    LinkGenerator linkGenerator,
    PermissionService permissionService)
{
    private const string PendingMultisigSettingPrefix = "PendingMultisigSetup";

    public async Task PopulateSetupViewModel(MultisigSetupViewModel vm)
    {
        vm.MultisigScriptType ??= "p2wsh";
        vm.MultisigRequiredSigners ??= 2;
        var safeCount = vm.MultisigTotalSigners is > 0 and <= 15
            ? vm.MultisigTotalSigners.Value
            : 3;
        vm.MultisigTotalSigners = safeCount;
        vm.MultisigStoreUsers = await GetStoreUsers(vm.StoreId, vm.MultisigParticipantUserIds);
    }

    public async Task<List<MultisigStoreUserItem>> GetStoreUsers(string storeId, IEnumerable<string>? selectedUserIds = null)
    {
        var users = await storeRepository.GetStoreUsers(storeId);
        var selected = (selectedUserIds ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        return users
            .Where(user => CanParticipateInMultisigSetup(storeId, user))
            .Select(user => new MultisigStoreUserItem
            {
                UserId = user.Id,
                Email = user.Email,
                Name = user.UserBlob?.Name,
                Selected = selected.Contains(user.Id)
            })
            .OrderBy(user => string.IsNullOrWhiteSpace(user.Name) ? user.Email : user.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(user => user.Email, StringComparer.OrdinalIgnoreCase)
            .ThenBy(user => user.UserId, StringComparer.Ordinal)
            .ToList();
    }

    private bool CanParticipateInMultisigSetup(string storeId, StoreRepository.StoreUser user)
    {
        var storeRole = user.StoreRole;
        if (storeRole is null ||
            !Permission.TryCreatePermission(WalletPolicies.CanSignWalletTransactions, storeId, out var requiredPermission))
        {
            return false;
        }

        return storeRole.ToPermissionSet(storeId).HasPermission(requiredPermission, permissionService);
    }

    public async Task<PendingMultisigSetupContext?> GetPendingMultisigSetupContext(string storeId, string? multisigSetupId)
    {
        if (string.IsNullOrWhiteSpace(multisigSetupId))
            return null;

        await using var ctx = dbContextFactory.CreateContext();
        var row = await ctx.Database.GetDbConnection().QueryFirstOrDefaultAsync<(string? StoreId, string Name, string Value, uint XMin)>(
            """
            SELECT "StoreId", "Name", "Value", xmin
            FROM "StoreSettings"
            WHERE "StoreId"= @storeId
              AND "Name" LIKE @namePattern
              AND COALESCE("Value"->>'RequestId', "Value"->>'requestId') = @multisigSetupId
            LIMIT 1
            """,
            new
            {
                storeId,
                namePattern = $"{PendingMultisigSettingPrefix}-%",
                multisigSetupId
            });
        if (row.StoreId is null)
            return null;

        var pending = JsonConvert.DeserializeObject<PendingMultisigSetupData>(row.Value, storeRepository.SerializerSettings);
        if (pending is null ||
            pending.ExpiresAt < DateTimeOffset.UtcNow ||
            !string.Equals(pending.RequestId, multisigSetupId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(pending.StoreId) ||
            !string.Equals(pending.StoreId, row.StoreId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(pending.CryptoCode))
            return null;

        return new PendingMultisigSetupContext(row.Name, pending, row.XMin);
    }

    public static string GetPendingMultisigSettingName(string cryptoCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cryptoCode);
        return $"{PendingMultisigSettingPrefix}-{cryptoCode.ToUpperInvariant()}";
    }

    public bool HasOnChainWallet(StoreData store, string cryptoCode)
    {
        var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
        return handlers.Support(paymentMethodId) &&
               store.GetPaymentMethodConfig<DerivationSchemeSettings>(paymentMethodId, handlers) is not null;
    }

    public async Task<IReadOnlyList<MultisigInProgressViewModel>> GetInProgressForStore(IAuthorizationService authorizationService, StoreData store, ClaimsPrincipal user)
    {
        var result = new List<MultisigInProgressViewModel>();
        var setupAccess = await authorizationService.GetSetupAccess(store.Id, user, pending: null);
        var userId = user.GetId();
        var cryptoCodes = handlers.OfType<BitcoinLikePaymentHandler>()
            .Select(h => h.Network.CryptoCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var cryptoCode in cryptoCodes)
        {
            var pending = await storeRepository.GetSettingAsync<PendingMultisigSetupData>(store.Id, GetPendingMultisigSettingName(cryptoCode));
            if (pending is null || pending.ExpiresAt < DateTimeOffset.UtcNow)
                continue;

            if (!pending.ReplacesExistingWallet && HasOnChainWallet(store, cryptoCode))
                continue;

            var pendingAccess = setupAccess with { IsParticipant = pending.IsPendingParticipant(userId) };
            if (!pendingAccess.CanViewStatus)
                continue;

            result.Add(CreateInProgressViewModel(store.Id, user.GetId(), pending, pendingAccess.CanManageWalletSettings));
        }

        return result
            .OrderBy(m => m.ReadyToCreateWallet ? 0 : 1)
            .ThenBy(m => m.CanSubmitSignerKey ? 0 : 1)
            .ThenBy(m => m.ExpiresAt)
            .ToList();
    }

    public MultisigInProgressViewModel CreateInProgressViewModel(string storeId, string userId, PendingMultisigSetupData pending, bool canCreateWallet)
    {
        var participant = pending.Participants.FirstOrDefault(p => string.Equals(p.UserId, userId, StringComparison.Ordinal));
        var didParticipate = participant is not null;
        var yourKeySubmitted = !string.IsNullOrWhiteSpace(participant?.AccountKey);
        var submittedSigners = pending.Participants.Count(p => !string.IsNullOrWhiteSpace(p.AccountKey));
        var sessionUrl = linkGenerator.MultisigSetupSessionLink(pending.RequestId, pending.RequestBaseUrl);

        return new MultisigInProgressViewModel
        {
            StoreId = storeId,
            CryptoCode = pending.CryptoCode,
            RequestId = pending.RequestId,
            RequiredSigners = pending.RequiredSigners,
            TotalSigners = pending.TotalSigners,
            SubmittedSigners = submittedSigners,
            DidParticipate = didParticipate,
            YourKeySubmitted = yourKeySubmitted,
            ExpiresAt = pending.ExpiresAt,
            SessionUrl = sessionUrl,
            CanCreateWallet = canCreateWallet,
            Participants = pending.Participants
                .Select(p => new MultisigInProgressParticipantViewModel
                {
                    Email = p.Email,
                    Name = p.Name,
                    Submitted = !string.IsNullOrWhiteSpace(p.AccountKey)
                })
                .ToArray()
        };
    }
}
