using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer
{
    public static class UtilitiesExtensions
    {
        public static void AddRange<T>(this HashSet<T> hashSet, IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                hashSet.Add(item);
            }
        }
    }
}
