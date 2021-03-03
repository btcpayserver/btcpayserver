#if ALTCOINS
using System;
using BTCPayServer.Payments;
using NBitcoin;
using Nethereum.HdWallet;

namespace BTCPayServer.Services.Altcoins.Matic.Payments
{
    public class MaticSupportedPaymentMethod : ISupportedPaymentMethod
    {
        public string CryptoCode { get; set; }
        public string Seed { get; set; }
        public string Password { get; set; }
        public string XPub { get; set; }
        public PaymentMethodId PaymentId => new PaymentMethodId(CryptoCode, MaticPaymentType.Instance);
        public long CurrentIndex { get; set; }
        public string KeyPath { get; set; }

        public Func<int, string> GetWalletDerivator()
        {
            if (!string.IsNullOrEmpty(XPub))
            {
                try
                {
                    return new PublicWallet(XPub).GetAddress;
                }
                catch (Exception)
                {
                    return new PublicWallet(new BitcoinExtPubKey(XPub, Network.Main).ExtPubKey).GetAddress;
                }
            }
            else if (!string.IsNullOrEmpty(XPub))
            {
                return i => new Wallet(Seed, Password, KeyPath).GetAccount(i).Address;
            }

            return null;
        }
    }
}
#endif
