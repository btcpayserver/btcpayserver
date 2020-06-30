using System.Collections.Generic;
using NBitcoin;

namespace BTCPayServer
{
    public class TransactionComparer : IEqualityComparer<Transaction>
    {

        private static readonly TransactionComparer _Instance = new TransactionComparer();
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
