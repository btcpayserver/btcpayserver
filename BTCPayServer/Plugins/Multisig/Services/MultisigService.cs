#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Multisig.Models;
using BTCPayServer.Plugins.Wallets;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NBitcoin;
using Newtonsoft.Json.Linq;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Multisig.Services;

public class MultisigService(
    StoreRepository storeRepository,
    ApplicationDbContextFactory dbContextFactory,
    PaymentMethodHandlerDictionary handlers,
    LinkGenerator linkGenerator,
    PermissionService permissionService)
{
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

    public async Task<MultisigSetupData?> GetPendingMultisigSetupContext(string storeId, string? multisigSetupId)
    {
        if (string.IsNullOrWhiteSpace(multisigSetupId))
            return null;

        await using var ctx = dbContextFactory.CreateContext();
        var row = await ctx.Database.GetDbConnection().QueryFirstOrDefaultAsync<string>(
            """
            SELECT data
            FROM multisig_setups
            WHERE store_id = @storeId AND id = @multisigSetupId AND expires_at > NOW()
            """,
            new
            {
                storeId,
                multisigSetupId
            });
        if (row is null)
            return null;
        return (await ToSetupData([row], ctx))[0];
    }

    private async Task<MultisigSetupData[]> ToSetupData(string[] setupData, ApplicationDbContext ctx)
    {
        if (setupData.Length == 0)
            return Array.Empty<MultisigSetupData>();
        var setupDataObj = setupData
            .Select(row => JsonConvert.DeserializeObject<MultisigSetupData>(row, storeRepository.SerializerSettings)!)
            .ToArray();
        var participants = await GetParticipants(ctx, setupDataObj.Select(s => s.RequestId).ToArray());
        foreach (var (pending, participant) in setupDataObj.Zip(participants))
            pending.Participants = participant;
        return setupDataObj;
    }

    public async Task<MultisigSetupData[]> GetPendingMultisigSetup(string storeId)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var setups = await ctx.Database.GetDbConnection().QueryAsync<string>(
            """
            SELECT data
            FROM multisig_setups
            WHERE store_id = @storeId AND expires_at > NOW()
            """,
            new { storeId });
        return await ToSetupData(setups.ToArray(), ctx);
    }

    public async Task SavePendingMultisigSetup(MultisigSetupData pending)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var connection = ctx.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await connection.ExecuteAsync(
            """
            DELETE FROM multisig_setups
            WHERE store_id = @storeId AND crypto_code = @cryptoCode;
            INSERT INTO multisig_setups (id, store_id, crypto_code, data, expires_at)
            VALUES (@id, @storeId, @cryptoCode, @data::JSONB, @expiresAt);
            """,
            new
            {
                id = pending.RequestId,
                storeId = pending.StoreId,
                cryptoCode = pending.CryptoCode.ToUpperInvariant(),
                data = JsonConvert.SerializeObject(pending, storeRepository.SerializerSettings),
                expiresAt = pending.ExpiresAt
            }, transaction);

        await connection.ExecuteAsync(
            """
            INSERT INTO multisig_setups_participants (multisig_setup_id, user_id)
            VALUES (@setupId, @userId)
            """,
            pending.Participants.Select(p => new
            {
                setupId = pending.RequestId,
                userId = p.UserId
            }).ToArray(), transaction);
        await transaction.CommitAsync();
        pending.Participants = (await GetParticipants(ctx, pending.RequestId))[0];
    }

    public async Task UpdateParticipant(string storeId, string setupId, string userId, PendingMultisigSetupParticipantData participant)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var connection = ctx.Database.GetDbConnection();
        await connection.ExecuteAsync(
            """
            UPDATE multisig_setups_participants msp
            SET account_key = @accountKey, account_key_path = @accountKeyPath
            FROM multisig_setups ms
            WHERE msp.multisig_setup_id = ms.id
              AND msp.multisig_setup_id = @setupId
              AND msp.user_id = @userId
              AND ms.store_id = @storeId;
            """,
            new { setupId, userId, storeId, accountKey = participant.AccountKey, accountKeyPath = participant.AccountKeyPath.ToString() });
    }

    public async Task DeletePendingMultisigSetup(string storeId, string setupId)
    {
        await using var ctx = dbContextFactory.CreateContext();
        await ctx.Database.GetDbConnection().ExecuteAsync(
            """
            DELETE FROM multisig_setups
            WHERE store_id = @storeId AND id = @setupId
            """,
            new { storeId, setupId });
    }

    private async Task<List<PendingMultisigSetupParticipantData>[]> GetParticipants(ApplicationDbContext ctx, params string[] setupIds)
    {
        var rows = await ctx.Database.GetDbConnection().QueryAsync<string>(
            """
            SELECT
                COALESCE(
                    jsonb_agg(
                        jsonb_build_object(
                            'userId', p.user_id,
                            'email', u."Email",
                            'name', COALESCE(u."Blob2"->>'Name', u."Blob2"->>'name'),
                            'accountKey', p.account_key,
                            'accountKeyPath', p.account_key_path
                        )
                        ORDER BY p.user_id
                    ) FILTER (WHERE p.user_id IS NOT NULL),
                    '[]'::jsonb
                )::text AS participants
            FROM unnest(@setupIds) WITH ORDINALITY AS s(id, ord)
            LEFT JOIN multisig_setups_participants p ON p.multisig_setup_id = s.id
            LEFT JOIN "AspNetUsers" u ON u."Id" = p.user_id
            GROUP BY s.id, s.ord
            ORDER BY s.ord;
            """,
            new { setupIds });
        return rows
            .Select(JArray.Parse)
            .Select(arr => arr.Select(row => new PendingMultisigSetupParticipantData
            {
                UserId = row["userId"]!.Value<string>(),
                Email = row["email"]!.Value<string>(),
                Name = row["name"]!.Value<string>(),
                AccountKey = row["accountKey"]!.Value<string?>(),
                AccountKeyPath = row["accountKeyPath"]!.Value<string?>() is string path ? RootedKeyPath.Parse(path) : null
            }).ToList()).ToArray();
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

        var pendings = await GetPendingMultisigSetup(store.Id);
        foreach (var pending in pendings)
        {
            if (!pending.ReplacesExistingWallet && HasOnChainWallet(store, pending.CryptoCode))
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

    public MultisigInProgressViewModel CreateInProgressViewModel(string storeId, string userId, MultisigSetupData pending, bool canCreateWallet)
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
