using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer
{
    public class TransactionComparer : IEqualityComparer<Transaction>
    {

        private static TransactionComparer _Instance = new TransactionComparer();
        public static TransactionComparer Instance
        {
            get
            {
                return _Instance;
            }
        }
        public bool Equals(Transaction x, Transaction y)
        {
            return x.GetHash() == y.GetHash();
        }

        public int GetHashCode(Transaction obj)
        {
            return obj.GetHash().GetHashCode();
        }
    }
}
