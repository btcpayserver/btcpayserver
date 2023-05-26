
using System;

namespace BTCPayServer
{
    public class StoreRoles
    {
        [Obsolete("You should check authorization policies instead of roles")]
        public const string Owner = "Owner";
        [Obsolete("You should check authorization policies instead of roles")]
        public const string Guest = "Guest";
    }
}
