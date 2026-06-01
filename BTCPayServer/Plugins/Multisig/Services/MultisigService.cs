#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using NBitcoin;
using NBitcoin.DataEncoders;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Multisig.Services;

public class MultisigService(
    StoreRepository storeRepository,
    PaymentMethodHandlerDictionary handlers,
    IDataProtectionProvider dataProtectionProvider,
    IAuthorizationService authorizationService,
    PermissionService permissionService)
{
    private const string PendingMultisigSettingPrefix = "PendingMultisigSetup";
    private readonly IDataProtector _inviteProtector = dataProtectionProvider.CreateProtector("MultisigInviteLink");

    public async Task PopulateSetupViewModel(MultisigSetupViewModel vm, HttpContext httpContext)
    {
        vm.MultisigScriptType ??= "p2wsh";
        vm.MultisigRequiredSigners ??= 2;
        var safeCount = vm.MultisigTotalSigners is > 0 and <= 15
            ? vm.MultisigTotalSigners.Value
            : 3;
        vm.MultisigTotalSigners = safeCount;
        vm.MultisigSigners ??= Enumerable.Repeat(string.Empty, safeCount).ToArray();
        vm.MultisigSignerFingerprints ??= Enumerable.Repeat(string.Empty, safeCount).ToArray();
        vm.MultisigSignerKeyPaths ??= Enumerable.Repeat(string.Empty, safeCount).ToArray();
        vm.MultisigPendingSetup = string.IsNullOrEmpty(vm.MultisigRequestId)
            ? await GetLatestPendingMultisigSetup(vm.StoreId, vm.CryptoCode)
            : await GetPendingMultisigSetup(vm.StoreId, vm.CryptoCode, vm.MultisigRequestId);
        if (vm.MultisigPendingSetup is not null)
        {
            vm.MultisigRequiredSigners = vm.MultisigPendingSetup.RequiredSigners;
            vm.MultisigTotalSigners = vm.MultisigPendingSetup.TotalSigners;
            vm.MultisigScriptType = vm.MultisigPendingSetup.ScriptType;
            vm.MultisigParticipantUserIds = vm.MultisigPendingSetup.Participants?
                .Select(p => p.UserId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        vm.MultisigStoreUsers = await GetStoreUsers(vm.StoreId, vm.MultisigParticipantUserIds);
        vm.MultisigRequestId ??= vm.MultisigPendingSetup?.RequestId;
        vm.MultisigInviteLinks = CreateInviteLinks(httpContext, vm.StoreId, vm.CryptoCode, vm.MultisigPendingSetup);
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

    public async Task<PendingMultisigSetupData?> GetPendingMultisigSetup(string storeId, string cryptoCode, string? requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            return null;
        var pending = await storeRepository.GetSettingAsync<PendingMultisigSetupData>(storeId, GetPendingMultisigSettingName(cryptoCode));
        if (pending is null || !string.Equals(pending.RequestId, requestId, StringComparison.Ordinal))
            return null;
        return pending.ExpiresAt < DateTimeOffset.UtcNow ? null : pending;
    }

    private async Task<PendingMultisigSetupData?> GetLatestPendingMultisigSetup(string storeId, string cryptoCode)
    {
        var pending = await storeRepository.GetSettingAsync<PendingMultisigSetupData>(storeId, GetPendingMultisigSettingName(cryptoCode));
        if (pending is null)
            return null;
        return pending.ExpiresAt < DateTimeOffset.UtcNow ? null : pending;
    }

    public static string GetPendingMultisigSettingName(string cryptoCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cryptoCode);
        return $"{PendingMultisigSettingPrefix}-{cryptoCode.ToUpperInvariant()}";
    }

    private string CreateInviteToken(string storeId, string cryptoCode, string requestId, string userId, DateTimeOffset expiresAt)
    {
        var payload = $"{storeId}|{cryptoCode}|{requestId}|{userId}|{expiresAt.ToUnixTimeSeconds()}";
        var protectedPayload = _inviteProtector.Protect(payload);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(protectedPayload));
    }

    public bool TryReadInviteToken(string token, out (string StoreId, string CryptoCode, string RequestId, string UserId) value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(token))
            return false;
        try
        {
            string protectedPayload;
            try
            {
                protectedPayload = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            }
            catch
            {
                protectedPayload = token;
            }

            var raw = _inviteProtector.Unprotect(protectedPayload);
            var parts = raw.Split('|');
            if (parts.Length != 5)
                return false;
            if (!long.TryParse(parts[4], out var expires))
                return false;
            if (DateTimeOffset.FromUnixTimeSeconds(expires) < DateTimeOffset.UtcNow)
                return false;
            value = (parts[0], parts[1], parts[2], parts[3]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Dictionary<string, string> CreateInviteLinks(HttpContext httpContext, string storeId, string cryptoCode, PendingMultisigSetupData? pending)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (pending?.Participants is null)
            return result;

        foreach (var participant in pending.Participants)
        {
            var link = CreateInviteLink(httpContext, storeId, cryptoCode, pending.RequestId, participant.UserId, pending.ExpiresAt, absolute: true);
            if (!string.IsNullOrEmpty(link))
                result[participant.UserId] = link;
        }
        return result;
    }

    public string? CreateInviteLink(HttpContext httpContext, string storeId, string cryptoCode, string requestId, string userId, DateTimeOffset expiresAt, bool absolute = false)
    {
        var token = CreateInviteToken(storeId, cryptoCode, requestId, userId, expiresAt);
        var path = $"{httpContext.Request.PathBase}/stores/{storeId}/onchain/{cryptoCode}/multisig/invite/{token}";
        return absolute && httpContext.Request.Host.HasValue
            ? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{path}"
            : path;
    }

    public string CreateSetupLink(HttpContext httpContext, string storeId, string cryptoCode, string requestId, bool absolute = false)
    {
        var values = new { area = MultisigPlugin.Area, storeId, cryptoCode, MultisigRequestId = requestId };
        var link = absolute
            ? linkGenerator.GetUriByAction(
                httpContext,
                nameof(UIMultisigSetupController.SetupMultisig),
                "UIMultisigSetup",
                values)
            : linkGenerator.GetPathByAction(
                httpContext,
                nameof(UIMultisigSetupController.SetupMultisig),
                "UIMultisigSetup",
                values);
        return link ?? throw new InvalidOperationException("Unable to generate multisig setup link.");
    }

    public async Task<IReadOnlyList<MultisigInProgressViewModel>> GetInProgressForStore(StoreData store, string userId, HttpContext httpContext)
    {
        var result = new List<MultisigInProgressViewModel>();
        var cryptoCodes = handlers.OfType<BitcoinLikePaymentHandler>()
            .Select(h => h.Network.CryptoCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var cryptoCode in cryptoCodes)
        {
            var pending = await storeRepository.GetSettingAsync<PendingMultisigSetupData>(store.Id, GetPendingMultisigSettingName(cryptoCode));
            if (pending is null || pending.ExpiresAt < DateTimeOffset.UtcNow)
                continue;

            var requestedCryptoCode = pending.CryptoCode ?? cryptoCode;
            var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(requestedCryptoCode);
            if (handlers.Support(paymentMethodId) &&
                store.GetPaymentMethodConfig<DerivationSchemeSettings>(paymentMethodId, handlers) is not null)
                continue;

            result.Add(CreateInProgressViewModel(store.Id, store.StoreName, userId, requestedCryptoCode, pending, httpContext, includeSetupUrl: true));
        }

        return result
            .OrderBy(m => m.ReadyToCreateWallet ? 0 : 1)
            .ThenBy(m => m.CanSubmitSignerKey ? 0 : 1)
            .ThenBy(m => m.ExpiresAt)
            .ToList();
    }

    public async Task<IReadOnlyList<MultisigInProgressViewModel>> GetInProgressForUser(IEnumerable<StoreData> stores, ClaimsPrincipal user, string userId, HttpContext httpContext)
    {
        var result = new List<MultisigInProgressViewModel>();
        var storesById = stores.ToDictionary(s => s.Id, s => s, StringComparer.Ordinal);
        if (storesById.Count == 0)
            return result;

        var cryptoCodes = handlers.OfType<BitcoinLikePaymentHandler>()
            .Select(h => h.Network.CryptoCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var cryptoCode in cryptoCodes)
        {
            var settings = await storeRepository.GetSettingsAsync<PendingMultisigSetupData>(GetPendingMultisigSettingName(cryptoCode));
            foreach (var (storeId, pending) in settings)
            {
                if (!storesById.TryGetValue(storeId, out var store) || pending is null)
                    continue;
                if (pending.ExpiresAt < DateTimeOffset.UtcNow)
                    continue;

                var requestedCryptoCode = pending.CryptoCode ?? cryptoCode;
                var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(requestedCryptoCode);
                if (handlers.Support(paymentMethodId) &&
                    store.GetPaymentMethodConfig<DerivationSchemeSettings>(paymentMethodId, handlers) is not null)
                    continue;

                var participant = pending.Participants.FirstOrDefault(p => string.Equals(p.UserId, userId, StringComparison.Ordinal));
                if (participant is null)
                    continue;

                var setupUrl = (await authorizationService.AuthorizeAsync(user, storeId, WalletPolicies.CanManageWalletSettings)).Succeeded
                    ? CreateSetupLink(httpContext, storeId, requestedCryptoCode, pending.RequestId)
                    : null;

                result.Add(CreateInProgressViewModel(storeId, store.StoreName, userId, requestedCryptoCode, pending, httpContext, setupUrl));
            }
        }

        return result
            .OrderBy(m => m.YourKeySubmitted)
            .ThenBy(m => m.ExpiresAt)
            .ThenBy(m => m.StoreName)
            .ToList();
    }

    public bool TryBuildDerivationScheme(MultisigSetupViewModel vm, BTCPayNetwork network, out string derivationScheme, out string validationError)
    {
        derivationScheme = string.Empty;
        validationError = string.Empty;

        var requiredSigners = vm.MultisigRequiredSigners ?? 0;
        var totalSigners = vm.MultisigTotalSigners ?? 0;
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

        var rawSignerKeys = vm.MultisigSigners ?? Array.Empty<string>();
        var rawFingerprints = vm.MultisigSignerFingerprints ?? Array.Empty<string>();
        var rawKeyPaths = vm.MultisigSignerKeyPaths ?? Array.Empty<string>();

        var signerKeys = rawSignerKeys
            .Select(k => k.Trim())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToArray()!;

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

        var suffix = vm.MultisigScriptType?.ToLowerInvariant() switch
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
            var fp = (rawFingerprints.ElementAtOrDefault(i) ?? string.Empty).Trim();
            var path = (rawKeyPaths.ElementAtOrDefault(i) ?? string.Empty).Trim();
            var hasOrigin = !string.IsNullOrWhiteSpace(fp) || !string.IsNullOrWhiteSpace(path);
            if (hasOrigin && (string.IsNullOrWhiteSpace(fp) || string.IsNullOrWhiteSpace(path)))
                hasPartialOriginInfo = true;

            if (!hasOrigin)
                continue;

            if (!Regex.IsMatch(fp, "^[0-9a-fA-F]{8}$"))
            {
                validationError = $"Signer {i + 1}: invalid fingerprint.";
                return false;
            }

            if (!TryParseKeyPath(path, out _))
            {
                validationError = $"Signer {i + 1}: invalid account key path.";
                return false;
            }
        }

        if (hasPartialOriginInfo)
        {
            validationError = "For each signer, provide both fingerprint and account key path, or leave both empty.";
            return false;
        }

        derivationScheme = $"{requiredSigners}-of-{string.Join("-", signerAccountKeys.Select(k => k.ToString()))}{suffix}";
        return true;
    }

    public void ApplySignerOrigins(MultisigSetupViewModel vm, DerivationSchemeSettings strategy)
    {
        if (strategy.AccountKeySettings is null || strategy.AccountKeySettings.Length == 0)
            return;

        for (var i = 0; i < strategy.AccountKeySettings.Length; i++)
        {
            var fp = (vm.MultisigSignerFingerprints?.ElementAtOrDefault(i) ?? string.Empty).Trim();
            var path = (vm.MultisigSignerKeyPaths?.ElementAtOrDefault(i) ?? string.Empty).Trim();
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
        if (KeyPath.TryParse(normalizedPath, out var parsedKeyPath))
        {
            keyPath = parsedKeyPath!;
            return true;
        }
        return false;
    }

    private MultisigInProgressViewModel CreateInProgressViewModel(string storeId, string storeName, string userId, string cryptoCode, PendingMultisigSetupData pending, HttpContext httpContext, bool includeSetupUrl)
    {
        return CreateInProgressViewModel(storeId, storeName, userId, cryptoCode, pending, httpContext, includeSetupUrl ? CreateSetupLink(httpContext, storeId, cryptoCode, pending.RequestId) : null);
    }

    private MultisigInProgressViewModel CreateInProgressViewModel(string storeId, string storeName, string userId, string cryptoCode, PendingMultisigSetupData pending, HttpContext httpContext, string? setupUrl)
    {
        var participant = pending.Participants.FirstOrDefault(p => string.Equals(p.UserId, userId, StringComparison.Ordinal));
        var didParticipate = participant is not null;
        var yourKeySubmitted = !string.IsNullOrWhiteSpace(participant?.AccountKey);
        var submittedSigners = pending.Participants.Count(p => !string.IsNullOrWhiteSpace(p.AccountKey));

        return new MultisigInProgressViewModel
        {
            StoreId = storeId,
            StoreName = storeName,
            CryptoCode = cryptoCode,
            RequestId = pending.RequestId,
            ScriptType = pending.ScriptType,
            RequiredSigners = pending.RequiredSigners,
            TotalSigners = pending.TotalSigners,
            SubmittedSigners = submittedSigners,
            DidParticipate = didParticipate,
            YourKeySubmitted = yourKeySubmitted,
            ExpiresAt = pending.ExpiresAt,
            InviteUrl = didParticipate ? CreateInviteLink(httpContext, storeId, cryptoCode, pending.RequestId, userId, pending.ExpiresAt) : null,
            SetupUrl = setupUrl
        };
    }
}
