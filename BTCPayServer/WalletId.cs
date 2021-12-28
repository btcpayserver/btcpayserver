using System;
using System.Text.RegularExpressions;
using BTCPayServer.Payments;

namespace BTCPayServer
{
    public class WalletId
    {
        static readonly Regex _WalletStoreRegex = new Regex("^S-([a-zA-Z0-9]{30,60})-([a-zA-Z]{2,5})$");
        public static bool TryParse(string str, out WalletId walletId)
        {
            ArgumentNullException.ThrowIfNull(str);
            walletId = null;
            WalletId w = new WalletId();
            var match = _WalletStoreRegex.Match(str);
            if (!match.Success)
                return false;
            w.StoreId = match.Groups[1].Value;
            w.CryptoCode = match.Groups[2].Value.ToUpperInvariant();
            walletId = w;
            return true;
        }
        public WalletId()
        {

        }
        public WalletId(string storeId, string cryptoCode)
        {
            StoreId = storeId;
            CryptoCode = cryptoCode;
        }
        public string StoreId { get; set; }
        public string CryptoCode { get; set; }

        public PaymentMethodId GetPaymentMethodId()
        {
            return new PaymentMethodId(CryptoCode, PaymentTypes.BTCLike);
        }
        public override bool Equals(object obj)
        {
            WalletId item = obj as WalletId;
            if (item == null)
                return false;
            return ToString().Equals(item.ToString(), StringComparison.InvariantCulture);
        }

        public static WalletId Parse(string id)
        {
            if (TryParse(id, out var v))
                return v;
            throw new FormatException("Invalid WalletId");
        }

        public static bool operator ==(WalletId a, WalletId b)
        {
            if (System.Object.ReferenceEquals(a, b))
                return true;
            if (((object)a == null) || ((object)b == null))
                return false;
            return a.ToString() == b.ToString();
        }

        public static bool operator !=(WalletId a, WalletId b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return ToString().GetHashCode(StringComparison.Ordinal);
        }
        public override string ToString()
        {
            if (StoreId == null || CryptoCode == null)
                return "";
            return $"S-{StoreId}-{CryptoCode.ToUpperInvariant()}";
        }
    }
}
