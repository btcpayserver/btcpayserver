using BTCPayServer.Client.Models;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments.Free;

public class FreePaymentMethod : PaymentMethod, ISupportedPaymentMethod
{
    public FreePaymentMethod()
    {
        SetPaymentMethodDetails(new FreePaymentData.FreePaymentDetails());
        Rate = 1m;
    }

    public override PaymentMethodId GetId()
    {
        return PaymentId;
    }

    public PaymentMethodId PaymentId { get; } = new PaymentMethodId("BTC", FreePaymentData.FreePaymentType.Instance);


    public class FreePaymentData : CryptoPaymentData
    {
        public BTCPayNetworkBase Network { get; set; }

        public string GetPaymentId()
        {
            return "free";
        }

        public string[] GetSearchTerms()
        {
            return new[] {"free"};
        }

        public decimal GetValue()
        {
            return 0m;
        }

        public bool PaymentCompleted(PaymentEntity entity)
        {
            return true;
        }

        public bool PaymentConfirmed(PaymentEntity entity, SpeedPolicy speedPolicy)
        {
            return true;
        }

        public PaymentType GetPaymentType()
        {
            return FreePaymentType.Instance;
        }

        public string GetDestination()
        {
            return "free";
        }

        public class FreePaymentType : PaymentType
        {
            public static FreePaymentType Instance = new();

            public override string ToPrettyString()
            {
                return "free";
            }

            public override string GetId()
            {
                return "free";
            }

            public override CryptoPaymentData DeserializePaymentData(BTCPayNetworkBase network, string str)
            {
                return new FreePaymentData();
            }

            public override string SerializePaymentData(BTCPayNetworkBase network, CryptoPaymentData paymentData)
            {
                return "";
            }

            public override IPaymentMethodDetails DeserializePaymentMethodDetails(BTCPayNetworkBase network, string str)
            {
                return new FreePaymentDetails();
            }

            public override string SerializePaymentMethodDetails(BTCPayNetworkBase network,
                IPaymentMethodDetails details)
            {
                return "{}";
            }

            public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkBase network,
                JToken value)
            {
                return new FreePaymentMethod();
            }

            public override string GetTransactionLink(BTCPayNetworkBase network, string txId)
            {
                return null;
            }

            public override string GetPaymentLink(BTCPayNetworkBase network, IPaymentMethodDetails paymentMethodDetails,
                Money cryptoInfoDue,
                string serverUri)
            {
                return null;
            }

            public override string InvoiceViewPaymentPartialName { get; }

            public override object GetGreenfieldData(ISupportedPaymentMethod supportedPaymentMethod,
                bool canModifyStore)
            {
                return null;
                ;
            }

            public override void PopulateCryptoInfo(PaymentMethod details, InvoiceCryptoInfo invoiceCryptoInfo,
                string serverUrl)
            {
            }
        }

        public class FreePaymentDetails : IPaymentMethodDetails
        {
            public string GetPaymentDestination()
            {
                return "";
            }

            public PaymentType GetPaymentType()
            {
                return FreePaymentType.Instance;
            }

            public decimal GetNextNetworkFee()
            {
                return 0;
            }

            public bool Activated { get; set; } = true;
        }
    }
}
