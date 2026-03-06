#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;

namespace BTCPayServer.Services.Stores;

public class MultisigRecipientsService(StoreRepository storeRepository)
{
    public async Task<string[]> GetWalletScopedRecipients(
        string storeId,
        string cryptoCode,
        IEnumerable<string>? allPolicies = null,
        IEnumerable<string>? anyPolicies = null,
        string? excludeUserId = null,
        IEnumerable<string>? includeUserIds = null)
    {
        if (string.IsNullOrWhiteSpace(storeId) || string.IsNullOrWhiteSpace(cryptoCode))
            return Array.Empty<string>();

        var requiredAll = (allPolicies ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var requiredAny = (anyPolicies ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var included = includeUserIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var walletTypePolicy = GetWalletTypePolicy(cryptoCode);

        var storeUsers = await storeRepository.GetStoreUsers(storeId);
        return storeUsers
            .Where(u => u is not null &&
                        !string.IsNullOrWhiteSpace(u.Id) &&
                        !string.IsNullOrWhiteSpace(u.Email))
            .Where(u => included is null || included.Contains(u.Id))
            .Where(u => excludeUserId is null || !string.Equals(u.Id, excludeUserId, StringComparison.Ordinal))
            .Where(u =>
            {
                var permissionSet = u.StoreRole?.ToPermissionSet(storeId) ?? new PermissionSet();
                if (!permissionSet.Contains(walletTypePolicy, storeId))
                    return false;
                if (requiredAll.Any(policy => !permissionSet.Contains(policy, storeId)))
                    return false;
                if (requiredAny.Length > 0 && !requiredAny.Any(policy => permissionSet.Contains(policy, storeId)))
                    return false;
                return true;
            })
            .Select(u => u.Email)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetWalletTypePolicy(string cryptoCode)
    {
        return cryptoCode.Equals("BTC", StringComparison.OrdinalIgnoreCase)
            ? Policies.CanModifyBitcoinOnchain
            : Policies.CanModifyOtherWallets;
    }
}
