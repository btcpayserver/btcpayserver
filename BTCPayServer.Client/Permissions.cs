using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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
        public const string CanModifyWebhooks = "btcpay.store.webhooks.canmodifywebhooks";
        public const string CanSendStoreEmail = "btcpay.store.cansendstoreemails";
        public const string CanModifyStoreSettingsUnscoped = "btcpay.store.canmodifystoresettings:";
        public const string CanViewStoreSettings = "btcpay.store.canviewstoresettings";
        public const string CanViewReports = "btcpay.store.canviewreports";
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
        public const string CanArchivePullPayments = "btcpay.store.canarchivepullpayments";
        public const string CanManagePayouts = "btcpay.store.canmanagepayouts";
        public const string CanViewPayouts = "btcpay.store.canviewpayouts";
        public const string CanCreatePullPayments = "btcpay.store.cancreatepullpayments";
        public const string CanViewPullPayments = "btcpay.store.canviewpullpayments";
        public const string CanCreateNonApprovedPullPayments = "btcpay.store.cancreatenonapprovedpullpayments";
        public const string Unrestricted = "unrestricted";
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
    }

    public enum PolicyType
    {
        Server,
        User,
        Store
    }

    public class Permission
    {
        private static readonly Regex _isPolicy = new Regex(@"^(?:btcpay\.([a-z0-9]+\.)*[a-z0-9]+|unrestricted)$", RegexOptions.Compiled);
        public PolicyType? Type { get; }
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
            if (!IsValidPolicy(policy))
                return false;
            permission = new Permission(policy, scope);
            return true;
        }


        public static Permission Parse(string str)
        {
            if (!TryParse(str, out var p))
                throw new FormatException("Invalid format for permission (Regex is ^(?:btcpay\\.([a-z0-9]+\\.)*[a-z0-9]+|unrestricted)$)");
            return p;
        }

        public static PolicyType? TryGetPolicyType(string policy)
        => Permission.TryParse(policy, out var permission) &&
            permission is
            {
                Scope: null
            } ? permission.Type : null;

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
                if (!IsValidPolicy(str))
                    return false;
                permission = new Permission(str, null);
                return true;
            }
            else
            {
                var policy = str.Substring(0, separator).ToLowerInvariant();
                var scope = str.Substring(separator + 1);
                if (scope.Length == 0)
                    return false;
                if (!IsValidPolicy(policy))
                    return false;
                permission = new Permission(policy, scope);
                return true;
            }
        }

        internal Permission(string policy, string scope)
        {
            Policy = policy;
            Scope = scope;
            if (policy.StartsWith("btcpay.store.", StringComparison.OrdinalIgnoreCase) ||
                policy.StartsWith("btcpay.plugin.store.", StringComparison.OrdinalIgnoreCase))
            {
                Type = PolicyType.Store;
            }
            else if (policy.StartsWith("btcpay.user.", StringComparison.OrdinalIgnoreCase) ||
                     policy.StartsWith("btcpay.plugin.user.", StringComparison.OrdinalIgnoreCase))
            {
                Type = PolicyType.User;
            }
            else if (policy.StartsWith("btcpay.server.", StringComparison.OrdinalIgnoreCase) ||
                     policy.StartsWith("btcpay.plugin.server.", StringComparison.OrdinalIgnoreCase))
            {
                Type = PolicyType.Server;
            }
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

        public Permission WithScope(string scope)
        => new Permission(Policy, scope);

        public static bool IsValidPolicy(string policy)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            return _isPolicy.IsMatch(policy);
        }
    }
}
