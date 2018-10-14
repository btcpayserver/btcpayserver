using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using NBitcoin;

namespace BTCPayServer.Models.InvoicingModels
{
    public class InvoiceDetailsModel
    {
        public class CryptoPayment
        {
            public string PaymentMethod { get; set; }
            public string Due { get; set; }
            public string Paid { get; set; }
            public string Address { get; internal set; }
            public string Rate { get; internal set; }
            public string PaymentUrl { get; internal set; }
            public string Overpaid { get; set; }
        }
        public class AddressModel
        {
            public string PaymentMethod { get; set; }
            public string Destination { get; set; }
            public bool Current { get; set; }
        }
        public class Payment
        {
            public string Crypto { get; set; }
            public string Confirmations
            {
                get; set;
            }
            public BitcoinAddress DepositAddress
            {
                get; set;
            }
            public string Amount
            {
                get; set;
            }
            public string TransactionId
            {
                get; set;
            }
            public DateTimeOffset ReceivedTime
            {
                get;
                internal set;
            }
            public string TransactionLink
            {
                get;
                set;
            }

            public bool Replaced { get; set; }
        }

        public string StatusMessage
        {
            get; set;
        }
        public String Id
        {
            get; set;
        }

        public List<CryptoPayment> CryptoPayments
        {
            get; set;
        } = new List<CryptoPayment>();

        public List<Payment> OnChainPayments { get; set; } = new List<Payment>();
        public List<OffChainPayment> OffChainPayments { get; set; } = new List<OffChainPayment>();
        public class OffChainPayment
        {
            public string Crypto { get; set; }
            public string BOLT11 { get; set; }
        }

        public string Status
        {
            get; set;
        }
        public string StatusException { get; set; }
        public DateTimeOffset CreatedDate
        {
            get; set;
        }

        public DateTimeOffset ExpirationDate
        {
            get; set;
        }

        public string OrderId
        {
            get; set;
        }
        public string RefundEmail
        {
            get;
            set;
        }
        public BuyerInformation BuyerInformation
        {
            get;
            set;
        }

        public string TransactionSpeed { get; set; }
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
        public ProductInformation ProductInformation
        {
            get;
            internal set;
        }
        public AddressModel[] Addresses { get; set; }
        public DateTimeOffset MonitoringDate { get; internal set; }
        public List<Data.InvoiceEventData> Events { get; internal set; }
        public string NotificationEmail { get; internal set; }
    }
}
