#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Multisig.Controllers;
using BTCPayServer.Plugins.Multisig.Models;
using BTCPayServer.Plugins.Wallets;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Multisig.Services;

public class MultisigService(
    StoreRepository storeRepository,
    ApplicationDbContextFactory dbContextFactory,
    PaymentMethodHandlerDictionary handlers,
    LinkGenerator linkGenerator,
    IAuthorizationService authorizationService,
    PermissionService permissionService)
{
    private const string PendingMultisigSettingPrefix = "PendingMultisigSetup";

    public readonly record struct MultisigSetupAccess(bool CanManageWalletSettings, bool CanSignWalletTransactions, bool IsParticipant)
    {
        public bool CanViewStatus => CanManageWalletSettings || (CanSignWalletTransactions && IsParticipant);
    }

    public static MultisigSetupAccess GetSetupAccess(bool canManageWalletSettings, bool canSignWalletTransactions, string? userId, PendingMultisigSetupData? pending)
    {
        return new MultisigSetupAccess(
            canManageWalletSettings,
            canSignWalletTransactions,
            IsPendingParticipant(pending, userId));
    }

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

    public async Task<PendingMultisigSetupContext?> GetPendingMultisigSetupContext(string? multisigSetupId)
    {
        if (string.IsNullOrWhiteSpace(multisigSetupId))
            return null;

        await using var ctx = dbContextFactory.CreateContext();
        var row = await ctx.Database.GetDbConnection().QueryFirstOrDefaultAsync<(string StoreId, string Name, string Value, uint XMin)>(
            """
            SELECT "StoreId", "Name", "Value", xmin
            FROM "StoreSettings"
            WHERE "Name" LIKE @namePattern
              AND COALESCE("Value"->>'RequestId', "Value"->>'requestId') = @multisigSetupId
            LIMIT 1
            """,
            new
            {
                namePattern = $"{PendingMultisigSettingPrefix}-%",
                multisigSetupId
            });
        if (row.Value is null)
            return null;

        var pending = JsonConvert.DeserializeObject<PendingMultisigSetupData>(row.Value, storeRepository.SerializerSettings);
        if (pending is null ||
            pending.ExpiresAt < DateTimeOffset.UtcNow ||
            !string.Equals(pending.RequestId, multisigSetupId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(pending.StoreId) ||
            !string.Equals(pending.StoreId, row.StoreId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(pending.CryptoCode))
            return null;

        return new PendingMultisigSetupContext(row.StoreId, pending.CryptoCode.ToUpperInvariant(), row.Name, pending, row.XMin);
    }

    public static string GetPendingMultisigSettingName(string cryptoCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cryptoCode);
        return $"{PendingMultisigSettingPrefix}-{cryptoCode.ToUpperInvariant()}";
    }

    public string CreateSessionLink(HttpContext httpContext, string requestId)
    {
        var values = new { area = MultisigPlugin.Area, multisigSetupId = requestId };
        var link = linkGenerator.GetUriByAction(
            httpContext,
            nameof(UIMultisigStatusController.Status),
            "UIMultisigStatus",
            values);
        return link ?? throw new InvalidOperationException("Unable to generate multisig setup link.");
    }

    public string CreateSignerKeyLink(HttpContext httpContext, string requestId)
    {
        var values = new { area = MultisigPlugin.Area, multisigSetupId = requestId };
        var link = linkGenerator.GetPathByAction(
            httpContext,
            nameof(UIMultisigSignerKeyController.SubmitMultisigSigner),
            "UIMultisigSignerKey",
            values);
        return link ?? throw new InvalidOperationException("Unable to generate multisig signer key link.");
    }

    public bool HasOnChainWallet(StoreData store, string cryptoCode)
    {
        var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
        return handlers.Support(paymentMethodId) &&
               store.GetPaymentMethodConfig<DerivationSchemeSettings>(paymentMethodId, handlers) is not null;
    }

    public async Task<MultisigSetupAccess> GetSetupAccess(string storeId, ClaimsPrincipal user, string? userId, PendingMultisigSetupData? pending)
    {
        var canManageWalletSettings = (await authorizationService.AuthorizeAsync(user, storeId, WalletPolicies.CanManageWalletSettings)).Succeeded;
        var canSignWalletTransactions = (await authorizationService.AuthorizeAsync(user, storeId, WalletPolicies.CanSignWalletTransactions)).Succeeded;
        return GetSetupAccess(canManageWalletSettings, canSignWalletTransactions, userId, pending);
    }

    public async Task<IReadOnlyList<MultisigInProgressViewModel>> GetInProgressForStore(StoreData store, ClaimsPrincipal user, string userId, HttpContext httpContext)
    {
        var result = new List<MultisigInProgressViewModel>();
        var setupAccess = await GetSetupAccess(store.Id, user, userId, pending: null);
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

            var pendingAccess = setupAccess with { IsParticipant = IsPendingParticipant(pending, userId) };
            if (!pendingAccess.CanViewStatus)
                continue;

            result.Add(CreateInProgressViewModel(store.Id, userId, cryptoCode, pending, httpContext, pendingAccess.CanManageWalletSettings));
        }

        return result
            .OrderBy(m => m.ReadyToCreateWallet ? 0 : 1)
            .ThenBy(m => m.CanSubmitSignerKey ? 0 : 1)
            .ThenBy(m => m.ExpiresAt)
            .ToList();
    }

    public bool TryBuildDerivationScheme(
        int requiredSigners,
        int totalSigners,
        string? scriptType,
        IReadOnlyList<PendingMultisigSetupParticipantData> participants,
        BTCPayNetwork network,
        out string derivationScheme,
        out string validationError)
    {
        derivationScheme = string.Empty;
        validationError = string.Empty;

        if (requiredSigners <= 0 || totalSigners <= 0 || requiredSigners > totalSigners)
        {
            validationError = "Invalid M-of-N configuration.";
            return false;
        }

        if (totalSigners > 15)
        {
            validationError = "Too many signers. Use 15 or fewer keys.";
            return false;
        }

        var signerKeys = participants
            .Select(p => p.AccountKey?.Trim())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k!)
            .ToArray();

        if (signerKeys.Length != totalSigners)
        {
            validationError = "Please provide all signer keys.";
            return false;
        }

        var signerAccountKeys = new BitcoinExtPubKey[signerKeys.Length];
        for (var i = 0; i < signerKeys.Length; i++)
        {
            if (!TryParseAccountKey(signerKeys[i], network, out var accountKey))
            {
                validationError = $"Signer {i + 1}: invalid account key.";
                return false;
            }

            signerAccountKeys[i] = accountKey;
        }

        if (signerAccountKeys.Select(k => k.ToString()).Distinct(StringComparer.Ordinal).Count() != signerAccountKeys.Length)
        {
            validationError = "Signer keys must be unique.";
            return false;
        }

        var suffix = scriptType?.ToLowerInvariant() switch
        {
            "p2wsh" => string.Empty,
            "p2sh-p2wsh" => "-[p2sh]",
            "p2sh" => "-[legacy]",
            _ => null
        };
        if (suffix is null)
        {
            validationError = "Invalid multisig script type.";
            return false;
        }

        var hasPartialOriginInfo = false;
        for (var i = 0; i < totalSigners; i++)
        {
            var participant = participants.ElementAtOrDefault(i);
            var fp = participant?.MasterFingerprint?.Trim() ?? string.Empty;
            var path = participant?.AccountKeyPath?.Trim() ?? string.Empty;
            var hasOrigin = !string.IsNullOrWhiteSpace(fp) || !string.IsNullOrWhiteSpace(path);
            switch (hasOrigin)
            {
                case true when (string.IsNullOrWhiteSpace(fp) || string.IsNullOrWhiteSpace(path)):
                    hasPartialOriginInfo = true;
                    break;
                case false:
                    continue;
            }

            if (!Regex.IsMatch(fp, "^[0-9a-fA-F]{8}$"))
            {
                validationError = $"Signer {i + 1}: invalid fingerprint.";
                return false;
            }

            if (TryParseKeyPath(path, out _)) continue;
            validationError = $"Signer {i + 1}: invalid account key path.";
            return false;
        }

        if (hasPartialOriginInfo)
        {
            validationError = "For each signer, provide both fingerprint and account key path, or leave both empty.";
            return false;
        }

        derivationScheme = $"{requiredSigners}-of-{string.Join("-", signerAccountKeys.Select(k => k.ToString()))}{suffix}";
        return true;
    }

    public void ApplySignerOrigins(IReadOnlyList<PendingMultisigSetupParticipantData> participants, DerivationSchemeSettings strategy)
    {
        if (strategy.AccountKeySettings is null || strategy.AccountKeySettings.Length == 0)
            return;

        for (var i = 0; i < strategy.AccountKeySettings.Length; i++)
        {
            var participant = participants.ElementAtOrDefault(i);
            var fp = participant?.MasterFingerprint?.Trim() ?? string.Empty;
            var path = participant?.AccountKeyPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(fp) || string.IsNullOrWhiteSpace(path))
                continue;

            if (!TryParseKeyPath(path, out var parsedPath) || !Regex.IsMatch(fp, "^[0-9a-fA-F]{8}$"))
                continue;

            strategy.AccountKeySettings[i].AccountKeyPath = parsedPath;
            strategy.AccountKeySettings[i].RootFingerprint = new HDFingerprint(Encoders.Hex.DecodeData(fp));
        }
    }

    public void ApplySignerIdentities(PendingMultisigSetupData pending, DerivationSchemeSettings strategy, BTCPayNetwork network)
    {
        if (pending.Participants is null || pending.Participants.Count == 0 || strategy.AccountKeySettings is null || strategy.AccountKeySettings.Length == 0)
            return;

        var participantsByKey = new Dictionary<string, PendingMultisigSetupParticipantData>(StringComparer.Ordinal);
        foreach (var participant in pending.Participants)
        {
            if (!TryParseAccountKey(participant.AccountKey, network, out var participantAccountKey))
                continue;

            participantsByKey.TryAdd(participantAccountKey.ToString(), participant);
        }

        foreach (var accountSettings in strategy.AccountKeySettings)
        {
            if (accountSettings.AccountKey is null ||
                !participantsByKey.TryGetValue(accountSettings.AccountKey.ToString(), out var participant))
                continue;

            accountSettings.SignerEmail = participant.Email;
        }
    }

    private static bool TryParseAccountKey(string? accountKey, BTCPayNetwork network, out BitcoinExtPubKey parsed)
    {
        parsed = null!;
        if (string.IsNullOrWhiteSpace(accountKey) || network.NBitcoinNetwork is null)
            return false;

        try
        {
            parsed = new BitcoinExtPubKey(accountKey.Trim(), network.NBitcoinNetwork);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryParseKeyPath(string? path, out KeyPath keyPath)
    {
        keyPath = null!;
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var normalizedPath = path
            .Replace("’", "'")
            .Replace("`", "'")
            .Replace("′", "'")
            .Replace(" ", string.Empty);
        normalizedPath = Regex.Replace(normalizedPath, @"([0-9]+)[hH]", "$1'");
        normalizedPath = normalizedPath.StartsWith("m/", StringComparison.OrdinalIgnoreCase) ? normalizedPath[2..] : normalizedPath;
        if (!KeyPath.TryParse(normalizedPath, out var parsedKeyPath)) return false;
        keyPath = parsedKeyPath!;
        return true;
    }

    public MultisigInProgressViewModel CreateInProgressViewModel(string storeId, string userId, string cryptoCode, PendingMultisigSetupData pending, HttpContext httpContext, bool canCreateWallet)
    {
        var participant = pending.Participants.FirstOrDefault(p => string.Equals(p.UserId, userId, StringComparison.Ordinal));
        var didParticipate = participant is not null;
        var yourKeySubmitted = !string.IsNullOrWhiteSpace(participant?.AccountKey);
        var submittedSigners = pending.Participants.Count(p => !string.IsNullOrWhiteSpace(p.AccountKey));
        var sessionUrl = CreateSessionLink(httpContext, pending.RequestId);

        return new MultisigInProgressViewModel
        {
            StoreId = storeId,
            CryptoCode = cryptoCode,
            RequestId = pending.RequestId,
            RequiredSigners = pending.RequiredSigners,
            TotalSigners = pending.TotalSigners,
            SubmittedSigners = submittedSigners,
            DidParticipate = didParticipate,
            YourKeySubmitted = yourKeySubmitted,
            ExpiresAt = pending.ExpiresAt,
            SessionUrl = sessionUrl,
            SignerKeyUrl = didParticipate ? CreateSignerKeyLink(httpContext, pending.RequestId) : null,
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

    private static bool IsPendingParticipant(PendingMultisigSetupData? pending, string? userId)
    {
        return !string.IsNullOrEmpty(userId) &&
               pending?.Participants.Any(p => string.Equals(p.UserId, userId, StringComparison.Ordinal)) is true;
    }
}
