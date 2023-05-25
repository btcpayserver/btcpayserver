using System;
using System.Collections.Generic;
using System.Text;
using BTCPayServer.Client.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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

        public string DerivationStrategies { get; set; }

        public string StoreName { get; set; }

        public SpeedPolicy SpeedPolicy { get; set; } = SpeedPolicy.MediumSpeed;

        public string StoreWebsite { get; set; }

        public byte[] StoreCertificate { get; set; }

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
        public IEnumerable<FormData> Forms { get; set; }
        public IEnumerable<StoreRole> StoreRoles { get; set; }

        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            if (databaseFacade.IsNpgsql())
            {
                builder.Entity<StoreData>()
                    .Property(o => o.StoreBlob)
                    .HasColumnType("JSONB");

                builder.Entity<StoreData>()
                    .Property(o => o.DerivationStrategies)
                    .HasColumnType("JSONB");
            }
            else if (databaseFacade.IsMySql())
            {
                builder.Entity<StoreData>()
                    .Property(o => o.StoreBlob)
                    .HasConversion(new ValueConverter<string, byte[]>
                    (
                        convertToProviderExpression: (str) => Encoding.UTF8.GetBytes(str),
                        convertFromProviderExpression: (bytes) => Encoding.UTF8.GetString(bytes)
                    ));
            }
        }
    }
}
