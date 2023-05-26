using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;

namespace BTCPayServer.Client
{
    public class Policies
    {
        public const string CanViewLightningInvoiceInternalNode = "btcpay.server.canviewlightninginvoiceinternalnode";
        public const string CanCreateLightningInvoiceInternalNode = "btcpay.server.cancreatelightninginvoiceinternalnode";
        public const string CanViewLightningInvoiceInStore = "btcpay.store.canviewlightninginvoice";
        public const string CanCreateLightningInvoiceInStore = "btcpay.store.cancreatelightninginvoice";
        public const string CanUseInternalLightningNode = "btcpay.server.canuseinternallightningnode";
        public const string CanUseLightningNodeInStore = "btcpay.store.canuselightningnode";
        public const string CanModifyServerSettings = "btcpay.server.canmodifyserversettings";
        public const string CanModifyStoreSettings = "btcpay.store.canmodifystoresettings";
        public const string CanModifyStoreWebhooks = "btcpay.store.webhooks.canmodifywebhooks";
        public const string CanModifyStoreSettingsUnscoped = "btcpay.store.canmodifystoresettings:";
        public const string CanViewStoreSettings = "btcpay.store.canviewstoresettings";
        public const string CanViewInvoices = "btcpay.store.canviewinvoices";
        public const string CanCreateInvoice = "btcpay.store.cancreateinvoice";
        public const string CanModifyInvoices = "btcpay.store.canmodifyinvoices";
        public const string CanViewPaymentRequests = "btcpay.store.canviewpaymentrequests";
        public const string CanModifyPaymentRequests = "btcpay.store.canmodifypaymentrequests";
        public const string CanModifyProfile = "btcpay.user.canmodifyprofile";
        public const string CanViewProfile = "btcpay.user.canviewprofile";
        public const string CanManageNotificationsForUser = "btcpay.user.canmanagenotificationsforuser";
        public const string CanViewNotificationsForUser = "btcpay.user.canviewnotificationsforuser";
        public const string CanViewUsers = "btcpay.server.canviewusers";
        public const string CanCreateUser = "btcpay.server.cancreateuser";
        public const string CanManageUsers = "btcpay.server.canmanageusers";
        public const string CanDeleteUser = "btcpay.user.candeleteuser";
        public const string CanManagePullPayments = "btcpay.store.canmanagepullpayments";
        public const string CanCreatePullPayments = "btcpay.store.cancreatepullpayments";
        public const string CanCreateNonApprovedPullPayments = "btcpay.store.cancreatenonapprovedpullpayments";
        public const string CanViewCustodianAccounts = "btcpay.store.canviewcustodianaccounts";
        public const string CanManageCustodianAccounts = "btcpay.store.canmanagecustodianaccounts";
        public const string CanDepositToCustodianAccounts = "btcpay.store.candeposittocustodianaccount";
        public const string CanWithdrawFromCustodianAccounts = "btcpay.store.canwithdrawfromcustodianaccount";
        public const string CanTradeCustodianAccount = "btcpay.store.cantradecustodianaccount";
        public const string Unrestricted = "unrestricted";
        public static IEnumerable<string> AllPolicies
        {
            get
            {
                yield return CanViewInvoices;
                yield return CanCreateInvoice;
                yield return CanModifyInvoices;
                yield return CanModifyStoreWebhooks;
                yield return CanModifyServerSettings;
                yield return CanModifyStoreSettings;
                yield return CanViewStoreSettings;
                yield return CanViewPaymentRequests;
                yield return CanModifyPaymentRequests;
                yield return CanModifyProfile;
                yield return CanViewProfile;
                yield return CanViewUsers;
                yield return CanCreateUser;
                yield return CanDeleteUser;
                yield return CanManageNotificationsForUser;
                yield return CanViewNotificationsForUser;
                yield return Unrestricted;
                yield return CanUseInternalLightningNode;
                yield return CanViewLightningInvoiceInternalNode;
                yield return CanCreateLightningInvoiceInternalNode;
                yield return CanUseLightningNodeInStore;
                yield return CanViewLightningInvoiceInStore;
                yield return CanCreateLightningInvoiceInStore;
                yield return CanManagePullPayments;
                yield return CanCreatePullPayments;
                yield return CanCreateNonApprovedPullPayments;
                yield return CanViewCustodianAccounts;
                yield return CanManageCustodianAccounts;
                yield return CanDepositToCustodianAccounts;
                yield return CanWithdrawFromCustodianAccounts;
                yield return CanTradeCustodianAccount;
                yield return CanManageUsers;
            }
        }
        public static bool IsValidPolicy(string policy)
        {
            return AllPolicies.Any(p => p.Equals(policy, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsStorePolicy(string policy)
        {
            return policy.StartsWith("btcpay.store", StringComparison.OrdinalIgnoreCase);
        }
        public static bool IsStoreModifyPolicy(string policy)
        {
            return policy.StartsWith("btcpay.store.canmodify", StringComparison.OrdinalIgnoreCase);
        }
        public static bool IsServerPolicy(string policy)
        {
            return policy.StartsWith("btcpay.server", StringComparison.OrdinalIgnoreCase);
        }
        public static bool IsPluginPolicy(string policy)
        {
            return policy.StartsWith("btcpay.plugin", StringComparison.OrdinalIgnoreCase);
        }
        public static bool IsUserPolicy(string policy)
        {
            return policy.StartsWith("btcpay.user", StringComparison.OrdinalIgnoreCase);
        }
    }

    public class PermissionSet
    {
        public PermissionSet() : this(Array.Empty<Permission>())
        {

        }
        public PermissionSet(Permission[] permissions)
        {
            Permissions = permissions;
        }

        public Permission[] Permissions { get; }

        public bool Contains(Permission requestedPermission)
        {
            return Permissions.Any(p => p.Contains(requestedPermission));
        }
        public bool Contains(string permission, string store)
        {
            if (permission is null)
                throw new ArgumentNullException(nameof(permission));
            if (store is null)
                throw new ArgumentNullException(nameof(store));
            return Contains(Permission.Create(permission, store));
        }
    }
    public class Permission
    {
        static Permission()
        {
            PolicyMap = Init();
        }

        public static Permission Create(string policy, string scope = null)
        {
            if (TryCreatePermission(policy, scope, out var r))
                return r;
            throw new ArgumentException("Invalid Permission");
        }

        public static bool TryCreatePermission(string policy, string scope, out Permission permission)
        {
            permission = null;
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            policy = policy.Trim().ToLowerInvariant();
            if (!Policies.IsValidPolicy(policy))
                return false;
            if (!string.IsNullOrEmpty(scope) && !Policies.IsStorePolicy(policy))
                return false;
            permission = new Permission(policy, scope);
            return true;
        }

        public static bool TryParse(string str, out Permission permission)
        {
            permission = null;
            if (str == null)
                throw new ArgumentNullException(nameof(str));
            str = str.Trim();
            var separator = str.IndexOf(':');
            if (separator == -1)
            {
                str = str.ToLowerInvariant();
                if (!Policies.IsValidPolicy(str))
                    return false;
                permission = new Permission(str, null);
                return true;
            }
            else
            {
                var policy = str.Substring(0, separator).ToLowerInvariant();
                if (!Policies.IsValidPolicy(policy))
                    return false;
                if (!Policies.IsStorePolicy(policy))
                    return false;
                var storeId = str.Substring(separator + 1);
                if (storeId.Length == 0)
                    return false;
                permission = new Permission(policy, storeId);
                return true;
            }
        }

        internal Permission(string policy, string scope)
        {
            Policy = policy;
            Scope = scope;
        }

        public bool Contains(Permission subpermission)
        {
            if (subpermission is null)
                throw new ArgumentNullException(nameof(subpermission));

            if (!ContainsPolicy(subpermission.Policy))
            {
                return false;
            }
            if (!Policies.IsStorePolicy(subpermission.Policy))
                return true;
            return Scope == null || subpermission.Scope == Scope;
        }

        public static IEnumerable<Permission> ToPermissions(string[] permissions)
        {
            if (permissions == null)
                throw new ArgumentNullException(nameof(permissions));
            foreach (var p in permissions)
            {
                if (TryParse(p, out var pp))
                    yield return pp;
            }
        }

        private bool ContainsPolicy(string subpolicy)
        {
            return ContainsPolicy(Policy, subpolicy);
        }

        private static bool ContainsPolicy(string policy, string subpolicy)
        {
            if (policy == Policies.Unrestricted)
                return true;
            if (policy == subpolicy)
                return true;
            if (!PolicyMap.TryGetValue(policy, out var subPolicies))
                return false;
            return subPolicies.Contains(subpolicy) || subPolicies.Any(s => ContainsPolicy(s, subpolicy));
        }

        public static ReadOnlyDictionary<string, HashSet<string>> PolicyMap { get; private set; }
    

        private static ReadOnlyDictionary<string, HashSet<string>> Init()
        {
            var policyMap = new Dictionary<string, HashSet<string>>();
            PolicyHasChild(policyMap, Policies.CanModifyStoreSettings,
                Policies.CanManageCustodianAccounts,
                Policies.CanManagePullPayments,
                Policies.CanModifyInvoices,
                Policies.CanViewStoreSettings,
                Policies.CanModifyStoreWebhooks,
                Policies.CanModifyPaymentRequests,
                Policies.CanUseLightningNodeInStore);

            PolicyHasChild(policyMap,Policies.CanManageUsers, Policies.CanCreateUser);
            PolicyHasChild(policyMap,Policies.CanManagePullPayments, Policies.CanCreatePullPayments);
            PolicyHasChild(policyMap,Policies.CanCreatePullPayments, Policies.CanCreateNonApprovedPullPayments);
            PolicyHasChild(policyMap,Policies.CanModifyPaymentRequests, Policies.CanViewPaymentRequests);
            PolicyHasChild(policyMap,Policies.CanModifyProfile, Policies.CanViewProfile);
            PolicyHasChild(policyMap,Policies.CanUseLightningNodeInStore, Policies.CanViewLightningInvoiceInStore, Policies.CanCreateLightningInvoiceInStore);
            PolicyHasChild(policyMap,Policies.CanManageNotificationsForUser, Policies.CanViewNotificationsForUser);
            PolicyHasChild(policyMap,Policies.CanModifyServerSettings,
                Policies.CanUseInternalLightningNode,
                Policies.CanManageUsers);
            PolicyHasChild(policyMap, Policies.CanUseInternalLightningNode, Policies.CanCreateLightningInvoiceInternalNode, Policies.CanViewLightningInvoiceInternalNode);
            PolicyHasChild(policyMap, Policies.CanManageCustodianAccounts, Policies.CanViewCustodianAccounts);
            PolicyHasChild(policyMap, Policies.CanModifyInvoices, Policies.CanViewInvoices, Policies.CanCreateInvoice, Policies.CanCreateLightningInvoiceInStore);
            PolicyHasChild(policyMap, Policies.CanViewStoreSettings, Policies.CanViewInvoices, Policies.CanViewPaymentRequests);

            var missingPolicies = Policies.AllPolicies.ToHashSet();
            //recurse through the tree to see which policies are not included in the tree
            foreach (var policy in policyMap)
            {
                missingPolicies.Remove(policy.Key);
                foreach (var subPolicy in policy.Value)
                {
                    missingPolicies.Remove(subPolicy);
                }
            }

            foreach (var missingPolicy in missingPolicies)
            {
                policyMap.Add(missingPolicy, new HashSet<string>());
            }
            return new ReadOnlyDictionary<string, HashSet<string>>(policyMap);
        }

        private static void PolicyHasChild(Dictionary<string, HashSet<string>>policyMap, string policy, params string[] subPolicies)
        {
            if (policyMap.TryGetValue(policy, out var existingSubPolicies))
            {
                foreach (string subPolicy in subPolicies)
                {
                    existingSubPolicies.Add(subPolicy);
                }
            }
            else
            {
                policyMap.Add(policy, subPolicies.ToHashSet());
            }
        }

        public string Scope { get; }
        public string Policy { get; }

        public override string ToString()
        {
            return Scope != null ? $"{Policy}:{Scope}" : Policy;
        }

        public override bool Equals(object obj)
        {
            Permission item = obj as Permission;
            return item != null && ToString().Equals(item.ToString());
        }
        public static bool operator ==(Permission a, Permission b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (((object)a == null) || ((object)b == null))
                return false;
            return a.ToString() == b.ToString();
        }

        public static bool operator !=(Permission a, Permission b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }
    }
}
