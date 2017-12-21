using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;
using NBitcoin;

namespace BTCPayServer.Models.InvoicingModels
{
    public class InvoiceDetailsModel
    {
        public class Payment
        {
            public int Confirmations
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
        }

        public string StatusMessage
        {
            get; set;
        }
        public String Id
        {
            get; set;
        }

        public List<Payment> Payments
        {
            get; set;
        } = new List<Payment>();

        public string Status
        {
            get; set;
        }

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
        public decimal Rate
        {
            get;
            internal set;
        }
        public string NotificationUrl
        {
            get;
            internal set;
        }
        public string Fiat
        {
            get;
            set;
        }
        public string BTC
        {
            get;
            set;
        }
        public string BTCDue
        {
            get;
            set;
        }
        public string BTCPaid
        {
            get;
            internal set;
        }
        public String NetworkFee
        {
            get;
            internal set;
        }
        public ProductInformation ProductInformation
        {
            get;
            internal set;
        }
        public BitcoinAddress BitcoinAddress
        {
            get;
            internal set;
        }
        public string PaymentUrl
        {
            get;
            set;
        }
    }
}
