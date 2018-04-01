using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Payments
{

    /// <summary>
    /// A value object which represent a crypto currency with his payment type (ie, onchain or offchain)
    /// </summary>
    public class PaymentMethodId
    {
        public PaymentMethodId(string cryptoCode, PaymentTypes paymentType)
        {
            if (cryptoCode == null)
                throw new ArgumentNullException(nameof(cryptoCode));
            PaymentType = paymentType;
            CryptoCode = cryptoCode;
        }

        [Obsolete("Should only be used for legacy stuff")]
        public bool IsBTCOnChain
        {
            get
            {
                return CryptoCode == "BTC" && PaymentType == PaymentTypes.BTCLike;
            }
        }

        public string CryptoCode { get; private set; }
        public PaymentTypes PaymentType { get; private set; }


        public override bool Equals(object obj)
        {
            PaymentMethodId item = obj as PaymentMethodId;
            if (item == null)
                return false;
            return ToString().Equals(item.ToString(), StringComparison.InvariantCulture);
        }
        public static bool operator ==(PaymentMethodId a, PaymentMethodId b)
        {
            if (System.Object.ReferenceEquals(a, b))
                return true;
            if (((object)a == null) || ((object)b == null))
                return false;
            return a.ToString() == b.ToString();
        }

        public static bool operator !=(PaymentMethodId a, PaymentMethodId b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
#pragma warning disable CA1307 // Specify StringComparison
            return ToString().GetHashCode();
#pragma warning restore CA1307 // Specify StringComparison
        }

        public override string ToString()
        {
            if (PaymentType == PaymentTypes.BTCLike)
                return CryptoCode;
            return CryptoCode + "_" + PaymentType.ToString();
        }

        public static PaymentMethodId Parse(string str)
        {
            var parts = str.Split('_');
            return new PaymentMethodId(parts[0], parts.Length == 1 ? PaymentTypes.BTCLike : Enum.Parse<PaymentTypes>(parts[1]));
        }
    }
}
