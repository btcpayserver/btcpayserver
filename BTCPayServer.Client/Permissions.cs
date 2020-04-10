using System;
using System.Collections.Generic;
using System.Linq;

namespace BTCPayServer.Client
{
    public class Policies
    {
        public const string CanModifyServerSettings = "btcpay.server.canmodifyserversettings";
        public const string CanModifyStoreSettings = "btcpay.store.canmodifystoresettings";
        public const string CanViewStoreSettings = "btcpay.store.canviewstoresettings";
        public const string CanCreateInvoice = "btcpay.store.cancreateinvoice";
        public const string CanModifyProfile = "btcpay.user.canmodifyprofile";
        public const string CanViewProfile = "btcpay.user.canviewprofile";
        public const string CanCreateUser = "btcpay.server.cancreateuser";
        public const string Unrestricted = "unrestricted";
        public static IEnumerable<string> AllPolicies
        {
            get
            {
                yield return CanCreateInvoice;
                yield return CanModifyServerSettings;
                yield return CanModifyStoreSettings;
                yield return CanViewStoreSettings;
                yield return CanModifyProfile;
                yield return CanViewProfile;
                yield return CanCreateUser;
                yield return Unrestricted;
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
        
        public static bool IsServerPolicy(string policy)
        {
            return policy.StartsWith("btcpay.server", StringComparison.OrdinalIgnoreCase);
        }
    }
    public class Permission
    {
        public static Permission Create(string policy, string storeId = null)
        {
            if (TryCreatePermission(policy, storeId, out var r))
                return r;
            throw new ArgumentException("Invalid Permission");
        }

        public static bool TryCreatePermission(string policy, string storeId, out Permission permission)
        {
            permission = null;
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            policy = policy.Trim().ToLowerInvariant();
            if (!Policies.IsValidPolicy(policy))
                return false;
            if (storeId != null && !Policies.IsStorePolicy(policy))
                return false;
            permission = new Permission(policy, storeId);
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

        

        internal Permission(string policy, string storeId)
        {
            Policy = policy;
            StoreId = storeId;
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
            return StoreId == null || subpermission.StoreId == this.StoreId;
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
            if (this.Policy == Policies.Unrestricted)
                return true;
            if (this.Policy == subpolicy)
                return true;
            if (subpolicy == Policies.CanViewStoreSettings && this.Policy == Policies.CanModifyStoreSettings)
                return true;
            if (subpolicy == Policies.CanCreateInvoice && this.Policy == Policies.CanModifyStoreSettings)
                return true;
            if (subpolicy == Policies.CanViewProfile && this.Policy == Policies.CanModifyProfile)
                return true;
            return false;
        }

        public string StoreId { get; }
        public string Policy { get; }

        public override string ToString()
        {
            if (StoreId != null)
            {
                return $"{Policy}:{StoreId}";
            }
            return Policy;
        }

        public override bool Equals(object obj)
        {
            Permission item = obj as Permission;
            if (item == null)
                return false;
            return ToString().Equals(item.ToString());
        }
        public static bool operator ==(Permission a, Permission b)
        {
            if (System.Object.ReferenceEquals(a, b))
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
