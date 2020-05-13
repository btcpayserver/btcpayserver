using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using Newtonsoft.Json;

namespace BTCPayServer.Data
{
    public static class WalletDataExtensions
    {

        public static PaymentMethodId GetPaymentMethodId(this WalletData walletData)
        {
            if (string.IsNullOrEmpty(walletData.CryptoCode) ||
                !PaymentTypes.TryParse(walletData.PaymentType, out var paymentType))
            {
                return null;
            }
            return new PaymentMethodId(walletData.CryptoCode, paymentType);
        }
        

        public static ISupportedPaymentMethod GetBlob(this WalletData walletData,
            BTCPayNetworkProvider btcPayNetworkProvider)
        {
            var networks = btcPayNetworkProvider.UnfilteredNetworks;
            var paymentMethodId = walletData.GetPaymentMethodId();
            var network = networks.GetNetwork<BTCPayNetworkBase>(paymentMethodId.CryptoCode);
            if (network == null)
            {
                return null;
            }

            return paymentMethodId.PaymentType.DeserializeSupportedPaymentMethod(network,
                ZipUtils.Unzip(walletData.Blob));
        }

        public static void SetBlob(this WalletData walletData, ISupportedPaymentMethod supportedPaymentMethod, BTCPayNetworkProvider btcPayNetworkProvider)
        {
            var paymentMethodId = walletData.GetPaymentMethodId();
            if (supportedPaymentMethod == null && paymentMethodId == null)
                throw new ArgumentException($"{nameof(supportedPaymentMethod)} or {nameof(paymentMethodId)} should be specified");
            
            if (paymentMethodId != null && paymentMethodId != supportedPaymentMethod.PaymentId)
            {
                throw new InvalidOperationException("Incoherent arguments, this should never happen");
            }

            if (paymentMethodId == null)
            {
                walletData.CryptoCode = supportedPaymentMethod.PaymentId.CryptoCode;
                walletData.PaymentType = supportedPaymentMethod.PaymentId.PaymentType.ToString();
            }

            walletData.Blob =
                ZipUtils.Zip(supportedPaymentMethod.PaymentId.PaymentType.SerializeSupportedPaymentMethod(
                    btcPayNetworkProvider.GetNetwork(walletData.CryptoCode), supportedPaymentMethod));
        }

        public static Dictionary<string, WalletTransactionInfo> GetWalletTransactionsInfo(this WalletData walletData)
        {
            if (walletData == null)
                throw new ArgumentNullException(nameof(walletData));
            return walletData.WalletTransactions.ToDictionary(w => w.TransactionId, w => w.GetBlobInfo());

        }

        public interface IWalletHasDefinedLabelColors
        {
            Dictionary<string, string> LabelColors { get; set; } 
        }
    }
}
