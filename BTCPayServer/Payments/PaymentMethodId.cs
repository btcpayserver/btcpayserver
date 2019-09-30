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
        public PaymentMethodId(string cryptoCode, PaymentType paymentType)
        {
            if (cryptoCode == null)
                throw new ArgumentNullException(nameof(cryptoCode));
            if (paymentType == null)
                throw new ArgumentNullException(nameof(paymentType));
            PaymentType = paymentType;
            CryptoCode = cryptoCode.ToUpperInvariant();
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
        public PaymentType PaymentType { get; private set; }


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
            //BTCLike case is special because it is in legacy mode.
            return PaymentType == PaymentTypes.BTCLike ? CryptoCode : $"{CryptoCode}_{PaymentType}";
        }

        public string ToPrettyString()
        {
            return $"{CryptoCode} ({PaymentType.ToPrettyString()})";
        }

        public static bool TryParse(string str, out PaymentMethodId paymentMethodId)
        {
            paymentMethodId = null;
            var parts = str.Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Length > 2)
                return false;
            PaymentType type = PaymentTypes.BTCLike;
            if (parts.Length == 2)
            {
                if (!PaymentTypes.TryParse(parts[1], out type))
                    return false;
            }
            paymentMethodId = new PaymentMethodId(parts[0], type);
            return true;
        }
        public static PaymentMethodId Parse(string str)
        {
            if (!TryParse(str, out var result))
                throw new FormatException("Invalid PaymentMethodId");
            return result;
        }
    }
}
