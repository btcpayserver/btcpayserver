using System;
using System.Collections.Generic;

namespace BTCPayServer
{
    public class StoreRoles
    {
        public const string Owner = "Owner";
        public const string Guest = "Guest";
        public static IEnumerable<String> AllRoles
        {
            get
            {
                yield return Owner;
                yield return Guest;
            }
        }
    }
}
