#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using BTCPayServer.Payments;

namespace BTCPayServer
{
    public record WalletId
    {
        static readonly Regex _WalletStoreRegex = new Regex("^S-([a-zA-Z0-9]{30,60})-([a-zA-Z]{2,5})$");
        public static bool TryParse(string str, [MaybeNullWhen(false)] out WalletId walletId)
        {
            ArgumentNullException.ThrowIfNull(str);
            walletId = null;
            var match = _WalletStoreRegex.Match(str);
            if (!match.Success)
                return false;
            var storeId = match.Groups[1].Value;
            var cryptoCode = match.Groups[2].Value.ToUpperInvariant();
            walletId = new WalletId(storeId, cryptoCode);
            return true;
        }
        public WalletId(string storeId, string cryptoCode)
        {
            ArgumentNullException.ThrowIfNull(storeId);
            ArgumentNullException.ThrowIfNull(cryptoCode);
            StoreId = storeId;
            CryptoCode = cryptoCode;
            PaymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(CryptoCode);
        }
        public string StoreId { get; }
        public string CryptoCode { get; }
        public PaymentMethodId PaymentMethodId { get; }
       
        public static WalletId Parse(string id)
        {
            if (TryParse(id, out var v))
                return v;
            throw new FormatException("Invalid WalletId");
        }

        public override string ToString()
        {
            if (StoreId == null || CryptoCode == null)
                return "";
            return $"S-{StoreId}-{CryptoCode.ToUpperInvariant()}";
        }
    }
}
