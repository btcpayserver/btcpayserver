using System;
using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Models.InvoicingModels
{
    public class OnchainPaymentViewModel
    {
        public PaymentMethodId PaymentMethodId { get; set; }
        public string Confirmations { get; set; }
        public BitcoinAddress DepositAddress { get; set; }
        public string Amount { get; set; }
        public string TransactionId { get; set; }
        public DateTimeOffset ReceivedTime { get; set; }
        public string TransactionLink { get; set; }

        public bool Replaced { get; set; }
        public BitcoinLikePaymentData CryptoPaymentData { get; set; }
        public decimal Value { get; set; }
        public string AdditionalInformation { get; set; }
        public decimal NetworkFee { get; set; }
        public string PaymentProof { get; set; }
        public string Currency { get; set; }
    }

    public class OffChainPaymentViewModel
    {
        public string Type { get; set; }
        public string BOLT11 { get; set; }
        public string Amount { get; set; }
        public string PaymentProof { get; set; }
    }

    public class InvoiceDetailsModel
    {
        public class CryptoPayment
        {
            public string PaymentMethod { get; set; }
            public string TotalDue { get; set; }
            public string Due { get; set; }
            public string Paid { get; set; }
            public string Address { get; internal set; }
            public string Rate { get; internal set; }
            public string PaymentUrl { get; internal set; }
            public string Overpaid { get; set; }
            [JsonIgnore]
            public PaymentMethodId PaymentMethodId { get; set; }

            public PaymentPrompt PaymentMethodRaw { get; set; }
        }
        public class AddressModel
        {
            public string PaymentMethod { get; set; }
            public string Destination { get; set; }
            public bool Current { get; set; }
        }
        public String Id
        {
            get; set;
        }

        public List<CryptoPayment> CryptoPayments
        {
            get; set;
        } = new List<CryptoPayment>();

        public InvoiceState State
        {
            get; set;
        }
        public InvoiceExceptionStatus StatusException { get; set; }
        public DateTimeOffset CreatedDate
        {
            get; set;
        }

        public DateTimeOffset ExpirationDate
        {
            get; set;
        }

        public List<StoreViewModels.DeliveryViewModel> Deliveries { get; set; } = new List<StoreViewModels.DeliveryViewModel>();
        public string TaxIncluded { get; set; }

        public string TransactionSpeed { get; set; }
        public string StoreId { get; set; }
        public object StoreName
        {
            get;
            internal set;
        }
        public string StoreLink
        {
            get;
            set;
        }

        public string PaymentRequestLink
        {
            get;
            set;
        }

        public string NotificationUrl
        {
            get;
            internal set;
        }

        public string RedirectUrl { get; set; }
        public string Fiat
        {
            get;
            set;
        }
        public InvoiceMetadata TypedMetadata { get; set; }
        public DateTimeOffset MonitoringDate { get; internal set; }
        public InvoiceEventData[] Events { get; internal set; }
        public string NotificationEmail { get; internal set; }
        public Dictionary<string, object> Metadata { get; set; }
        public Dictionary<string, object> ReceiptData { get; set; }
        public Dictionary<string, object> AdditionalData { get; set; }
        public List<PaymentEntity> Payments { get; set; }
        public bool Archived { get; set; }
        public bool CanRefund { get; set; }
        public bool ShowCheckout { get; set; }
        public List<RefundData> Refunds { get; set; }
        public bool ShowReceipt { get; set; }
        public bool Overpaid { get; set; }
        public bool HasRefund { get; set; }
        public bool StillDue { get; set; }
        public bool HasRates { get; set; }
        public InvoiceEntity Entity { get; internal set; }
    }
}
