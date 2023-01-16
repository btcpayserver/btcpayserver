using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using BTCPayServer.Client.Models;
using BTCPayServer.Data.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PayoutProcessorData = BTCPayServer.Data.Data.PayoutProcessorData;

namespace BTCPayServer.Data
{
    public class StoreData
    {
        public string Id { get; set; }
        public List<UserStore> UserStores { get; set; }

        public List<AppData> Apps { get; set; }

        public List<PaymentRequestData> PaymentRequests { get; set; }

        public List<PullPaymentData> PullPayments { get; set; }

        public List<InvoiceData> Invoices { get; set; }

        [Obsolete("Use GetDerivationStrategies instead")]
        public string DerivationStrategy { get; set; }

        [Obsolete("Use GetDerivationStrategies instead")]
        public string DerivationStrategies { get; set; }

        public string StoreName { get; set; }

        public SpeedPolicy SpeedPolicy { get; set; } = SpeedPolicy.MediumSpeed;

        public string StoreWebsite { get; set; }

        public byte[] StoreCertificate { get; set; }

        [NotMapped] public string Role { get; set; }

        public string StoreBlob { get; set; }

        [Obsolete("Use GetDefaultPaymentId instead")]
        public string DefaultCrypto { get; set; }

        public List<PairedSINData> PairedSINs { get; set; }
        public IEnumerable<APIKeyData> APIKeys { get; set; }
        public IEnumerable<LightningAddressData> LightningAddresses { get; set; }
        public IEnumerable<PayoutProcessorData> PayoutProcessors { get; set; }
        public IEnumerable<PayoutData> Payouts { get; set; }
        public IEnumerable<CustodianAccountData> CustodianAccounts { get; set; }
        public IEnumerable<StoreSettingData> Settings { get; set; }

        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            if (databaseFacade.IsNpgsql())
            {
                builder.Entity<StoreData>()
                    .Property(o => o.StoreBlob)
                    .HasColumnType("JSONB");
            }
        }
    }
}
