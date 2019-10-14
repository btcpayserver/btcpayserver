using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Security.Claims;

namespace BTCPayServer.Data
{
    public enum SpeedPolicy
    {
        HighSpeed = 0,
        MediumSpeed = 1,
        LowSpeed = 2,
        LowMediumSpeed = 3
    }
    public class StoreData
    {
        public string Id
        {
            get;
            set;
        }

        public List<UserStore> UserStores
        {
            get; set;
        }
        public List<AppData> Apps
        {
            get; set;
        }
        
        public List<PaymentRequestData> PaymentRequests
        {
            get; set;
        }

        public List<InvoiceData> Invoices { get; set; }

        [Obsolete("Use GetDerivationStrategies instead")]
        public string DerivationStrategy
        {
            get; set;
        }

        [Obsolete("Use GetDerivationStrategies instead")]
        public string DerivationStrategies
        {
            get;
            set;
        }

        public string StoreName
        {
            get; set;
        }

        public SpeedPolicy SpeedPolicy
        {
            get; set;
        }

        public string StoreWebsite
        {
            get; set;
        }

        public byte[] StoreCertificate
        {
            get; set;
        }

        [NotMapped]
        public string Role
        {
            get; set;
        }

        public byte[] StoreBlob
        {
            get;
            set;
        }
        [Obsolete("Use GetDefaultPaymentId instead")]
        public string DefaultCrypto { get; set; }
        public List<PairedSINData> PairedSINs { get; set; }
        public IEnumerable<APIKeyData> APIKeys { get; set; }
    }

    public enum NetworkFeeMode
    {
        MultiplePaymentsOnly,
        Always,
        Never
    }
}
