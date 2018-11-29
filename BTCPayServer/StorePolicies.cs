using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
