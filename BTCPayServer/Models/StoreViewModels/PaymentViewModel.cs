using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Client.Models;
using BTCPayServer.Validation;
using static BTCPayServer.Data.StoreBlob;

namespace BTCPayServer.Models.StoreViewModels
{
    public class PaymentViewModel
    {
        public List<StoreDerivationScheme> DerivationSchemes { get; set; }
        public List<StoreLightningNode> LightningNodes { get; set; }
        public bool IsOnchainSetup { get; set; }
        public bool IsLightningSetup { get; set; }
        public bool CanUsePayJoin { get; set; }
        
        [Display(Name = "Allow anyone to create invoice")]
        public bool AnyoneCanCreateInvoice { get; set; }

        [Display(Name = "Invoice expires if the full amount has not been paid after …")]
        [Range(1, 60 * 24 * 24)]
        public int InvoiceExpiration { get; set; }

        [Display(Name = "Payment invalid if transactions fails to confirm … after invoice expiration")]
        [Range(10, 60 * 24 * 24)]
        public int MonitoringExpiration { get; set; }

        [Display(Name = "Consider the invoice confirmed when the payment transaction …")]
        public SpeedPolicy SpeedPolicy { get; set; }

        [Display(Name = "Add additional fee (network fee) to invoice …")]
        public NetworkFeeMode NetworkFeeMode { get; set; }

        [Display(Name = "Description template of the lightning invoice")]
        public string LightningDescriptionTemplate { get; set; }

        [Display(Name = "Enable Payjoin/P2EP")]
        public bool PayJoinEnabled { get; set; }

        [Display(Name = "Show recommended fee")]
        public bool ShowRecommendedFee { get; set; }

        [Display(Name = "Recommended fee confirmation target blocks")]
        [Range(1, double.PositiveInfinity)]
        public int RecommendedFeeBlockTarget { get; set; }

        [Display(Name = "Display Lightning payment amounts in Satoshis")]
        public bool LightningAmountInSatoshi { get; set; }

        [Display(Name = "Add hop hints for private channels to the Lightning invoice")]
        public bool LightningPrivateRouteHints { get; set; }

        [Display(Name = "Include Lightning invoice fallback to on-chain BIP21 payment URL")]
        public bool OnChainWithLnInvoiceFallback { get; set; }

        [Display(Name = "Consider the invoice paid even if the paid amount is ... % less than expected")]
        [Range(0, 100)]
        public double PaymentTolerance { get; set; }
        
        [Display(Name = "Default currency")]
        [MaxLength(10)]
        public string DefaultCurrency { get; set; }
    }
}
