using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments;

namespace BTCPayServer.Services.Invoices
{
    public class CryptoDataDictionary : IEnumerable<CryptoData>
    {
        Dictionary<CryptoDataId, CryptoData> _Inner = new Dictionary<CryptoDataId, CryptoData>();
        public CryptoDataDictionary()
        {

        }

        public CryptoData this[CryptoDataId index]
        {
            get
            {
                return _Inner[index];
            }
        }

        public void Add(CryptoData cryptoData)
        {
            _Inner.Add(cryptoData.GetId(), cryptoData);
        }

        public void Remove(CryptoData cryptoData)
        {
            _Inner.Remove(cryptoData.GetId());
        }
        public bool TryGetValue(CryptoDataId cryptoDataId, out CryptoData data)
        {
            if (cryptoDataId == null)
                throw new ArgumentNullException(nameof(cryptoDataId));
            return _Inner.TryGetValue(cryptoDataId, out data);
        }

        public void AddOrReplace(CryptoData cryptoData)
        {
            var key = cryptoData.GetId();
            _Inner.Remove(key);
            _Inner.Add(key, cryptoData);
        }

        public IEnumerator<CryptoData> GetEnumerator()
        {
            return _Inner.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public CryptoData TryGet(CryptoDataId cryptoDataId)
        {
            if (cryptoDataId == null)
                throw new ArgumentNullException(nameof(cryptoDataId));
            _Inner.TryGetValue(cryptoDataId, out var value);
            return value;
        }
        public CryptoData TryGet(string network, PaymentTypes paymentType)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            var id = new CryptoDataId(network, paymentType);
            return TryGet(id);
        }
    }
}
