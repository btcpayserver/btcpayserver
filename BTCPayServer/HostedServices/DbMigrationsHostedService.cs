using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Labels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.HostedServices
{
    /// <summary>
    /// In charge of all long running db migrations that we can't execute on startup in MigrationStartupTask
    /// </summary>
    public class DbMigrationsHostedService : BaseAsyncService
    {
        private readonly InvoiceRepository _invoiceRepository;
        private readonly SettingsRepository _settingsRepository;
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly IOptions<DataDirectories> _datadirs;

        public DbMigrationsHostedService(InvoiceRepository invoiceRepository, SettingsRepository settingsRepository, ApplicationDbContextFactory dbContextFactory, IOptions<DataDirectories> datadirs, Logs logs) : base(logs)
        {
            _invoiceRepository = invoiceRepository;
            _settingsRepository = settingsRepository;
            _dbContextFactory = dbContextFactory;
            _datadirs = datadirs;
        }


        internal override Task[] InitializeTasks()
        {
            return new Task[] { ProcessMigration() };
        }

        protected async Task ProcessMigration()
        {
            var settings = await _settingsRepository.GetSettingAsync<MigrationSettings>();
            if (settings.MigratedInvoiceTextSearchPages != int.MaxValue)
            {
                await MigratedInvoiceTextSearchToDb(settings.MigratedInvoiceTextSearchPages ?? 0);
            }
            if (settings.MigratedTransactionLabels != int.MaxValue)
            {
                await MigratedTransactionLabels(settings.MigratedTransactionLabels ?? 0);
            }
        }


#pragma warning disable CS0612 // Type or member is obsolete
        class LegacyWalletTransactionInfo
        {
            public string Comment { get; set; } = string.Empty;
            [JsonIgnore]
            public Dictionary<string, LabelData> Labels { get; set; } = new Dictionary<string, LabelData>();
        }

        static LegacyWalletTransactionInfo GetBlobInfo(WalletTransactionData walletTransactionData)
        {
            LegacyWalletTransactionInfo blobInfo;
            if (walletTransactionData.Blob == null || walletTransactionData.Blob.Length == 0)
                blobInfo = new LegacyWalletTransactionInfo();
            else
                blobInfo = JsonConvert.DeserializeObject<LegacyWalletTransactionInfo>(ZipUtils.Unzip(walletTransactionData.Blob));
            if (!string.IsNullOrEmpty(walletTransactionData.Labels))
            {
                if (walletTransactionData.Labels.StartsWith('['))
                {
                    foreach (var jtoken in JArray.Parse(walletTransactionData.Labels))
                    {
                        var l = jtoken.Type == JTokenType.String ? Label.Parse(jtoken.Value<string>())
                                                                : Label.Parse(jtoken.ToString());
                        blobInfo.Labels.TryAdd(l.Text, l);
                    }
                }
                else
                {
                    // Legacy path
                    foreach (var token in walletTransactionData.Labels.Split(',',
                        StringSplitOptions.RemoveEmptyEntries))
                    {
                        var l = Label.Parse(token);
                        blobInfo.Labels.TryAdd(l.Text, l);
                    }
                }
            }
            return blobInfo;
        }

        internal async Task MigratedTransactionLabels(int startFromOffset)
        {
            int batchCount = 2000;
            int total = 0;
            HashSet<(string WalletId, string LabelId)> existingLabels;
            using (var db = _dbContextFactory.CreateContext())
            {
                total = await db.WalletTransactions.CountAsync();
                existingLabels = (await (
                    db.WalletObjects.AsNoTracking()
                    .Where(wo => wo.Type == WalletObjectData.Types.Label)
                    .Select(wl => new { wl.WalletId, wl.Id })
                    .ToListAsync()))
                    .Select(o => (o.WalletId, o.Id)).ToHashSet();
            }



next:
            using (var db = _dbContextFactory.CreateContext())
            {
                Logs.PayServer.LogInformation($"Wallet transaction label importing transactions {startFromOffset}/{total}");
                var txs = await db.WalletTransactions
                    .OrderByDescending(wt => wt.TransactionId)
                    .Skip(startFromOffset)
                    .Take(batchCount)
                    .ToArrayAsync();

                foreach (var tx in txs)
                {
                    var blob = GetBlobInfo(tx);
                    db.WalletObjects.Add(new Data.WalletObjectData()
                    {
                        WalletId = tx.WalletDataId,
                        Type = Data.WalletObjectData.Types.Tx,
                        Id = tx.TransactionId,
                        Data = string.IsNullOrEmpty(blob.Comment) ? null : new JObject() { ["comment"] = blob.Comment }.ToString()
                    });
                    foreach (var label in blob.Labels)
                    {
                        if (!existingLabels.Contains((tx.WalletDataId, label.Key)))
                        {
                            JObject labelData = new JObject();
                            labelData.Add("color", "#000");
                            db.WalletObjects.Add(new WalletObjectData()
                            {
                                WalletId = tx.WalletDataId,
                                Type = WalletObjectData.Types.Label,
                                Id = label.Key,
                                Data = labelData.ToString()
                            });
                            existingLabels.Add((tx.WalletDataId, label.Key));
                        }
                        db.WalletObjectLinks.Add(new WalletObjectLinkData()
                        {
                            WalletId = tx.WalletDataId,
                            ChildType = Data.WalletObjectData.Types.Tx,
                            ChildId = tx.TransactionId,
                            ParentType = Data.WalletObjectData.Types.Label,
                            ParentId = label.Key
                        });

                        if (label.Value is ReferenceLabel reflabel)
                        {
                            if (reflabel.Type == "invoice" ||
                                reflabel.Type == "payment-request" ||
                                reflabel.Type == "app" ||
                                reflabel.Type == "pj-exposed")
                            {
                                db.WalletObjects.Add(new WalletObjectData()
                                {
                                    WalletId = tx.WalletDataId,
                                    Type = reflabel.Type,
                                    Id = reflabel.Reference ?? String.Empty
                                });
                                db.WalletObjectLinks.Add(new WalletObjectLinkData()
                                {
                                    WalletId = tx.WalletDataId,
                                    ChildType = Data.WalletObjectData.Types.Tx,
                                    ChildId = tx.TransactionId,
                                    ParentType = reflabel.Type,
                                    ParentId = reflabel.Reference ?? String.Empty
                                });
                            }
                        }
                        else if (label.Value is PayoutLabel payoutLabel)
                        {
                            foreach (var pp in payoutLabel.PullPaymentPayouts)
                            {
                                foreach (var payout in pp.Value)
                                {
                                    var payoutData = string.IsNullOrEmpty(pp.Key) ? null : new JObject()
                                    {
                                        ["pullPaymentId"] = pp.Key
                                    };
                                    db.WalletObjects.Add(new WalletObjectData()
                                    {
                                        WalletId = tx.WalletDataId,
                                        Type = "payout",
                                        Id = payout,
                                        Data = payoutData.ToString()
                                    });
                                    db.WalletObjectLinks.Add(new WalletObjectLinkData()
                                    {
                                        WalletId = tx.WalletDataId,
                                        ChildType = Data.WalletObjectData.Types.Tx,
                                        ChildId = tx.TransactionId,
                                        ParentType = "payout",
                                        ParentId = payout
                                    });
                                }
                            }
                        }
                    }
                }
                await db.SaveChangesAsync();
                if (txs.Length < batchCount)
                {
                    var settings = await _settingsRepository.GetSettingAsync<MigrationSettings>();
                    settings.MigratedTransactionLabels = int.MaxValue;
                    await _settingsRepository.UpdateSetting(settings);
                    Logs.PayServer.LogInformation($"Wallet transaction label successfully migrated");
                    return;
                }
                else
                {
                    startFromOffset += batchCount;
                    var settings = await _settingsRepository.GetSettingAsync<MigrationSettings>();
                    settings.MigratedTransactionLabels = startFromOffset;
                    await _settingsRepository.UpdateSetting(settings);
                    goto next;
                }
            }
        }
#pragma warning restore CS0612 // Type or member is obsolete
        private async Task MigratedInvoiceTextSearchToDb(int startFromPage)
        {
            // deleting legacy DBriize database if present
            var dbpath = Path.Combine(_datadirs.Value.DataDir, "InvoiceDB");
            if (Directory.Exists(dbpath))
            {
                Directory.Delete(dbpath, true);
            }

            var invoiceQuery = new InvoiceQuery { IncludeArchived = true };
            var totalCount = await CountInvoices();
            const int PAGE_SIZE = 1000;
            var totalPages = Math.Ceiling(totalCount * 1.0m / PAGE_SIZE);
            Logs.PayServer.LogInformation($"Importing {totalCount} invoices into the search table in {totalPages - startFromPage} pages");
            for (int i = startFromPage; i < totalPages && !CancellationToken.IsCancellationRequested; i++)
            {
                Logs.PayServer.LogInformation($"Import to search table progress: {i + 1}/{totalPages} pages");
                // migrate data to new table using invoices from database
                using var ctx = _dbContextFactory.CreateContext();
                invoiceQuery.Skip = i * PAGE_SIZE;
                invoiceQuery.Take = PAGE_SIZE;
                var invoices = await _invoiceRepository.GetInvoices(invoiceQuery);

                foreach (var invoice in invoices)
                {
                    var textSearch = new List<string>();

                    // recreating different textSearch.Adds that were previously in DBriize
                    foreach (var paymentMethod in invoice.GetPaymentMethods())
                    {
                        if (paymentMethod.Network != null)
                        {
                            var paymentDestination = paymentMethod.GetPaymentMethodDetails().GetPaymentDestination();
                            textSearch.Add(paymentDestination);
                            textSearch.Add(paymentMethod.Calculate().TotalDue.ToString());
                        }
                    }
                    // 
                    textSearch.Add(invoice.Id);
                    textSearch.Add(invoice.InvoiceTime.ToString(CultureInfo.InvariantCulture));
                    textSearch.Add(invoice.Price.ToString(CultureInfo.InvariantCulture));
                    textSearch.Add(invoice.Metadata.OrderId);
                    textSearch.Add(invoice.StoreId);
                    textSearch.Add(invoice.Metadata.BuyerEmail);
                    //
                    textSearch.Add(invoice.RefundMail);
                    // TODO: Are there more things to cache? PaymentData?
                    InvoiceRepository.AddToTextSearch(ctx,
                        new Data.InvoiceData { Id = invoice.Id, InvoiceSearchData = new List<InvoiceSearchData>() },
                        textSearch.ToArray());
                }

                var settings = await _settingsRepository.GetSettingAsync<MigrationSettings>();
                if (i + 1 < totalPages)
                {
                    settings.MigratedInvoiceTextSearchPages = i;
                }
                else
                {
                    // during final pass we set int.MaxValue so migration doesn't run again
                    settings.MigratedInvoiceTextSearchPages = int.MaxValue;
                }

                // this call triggers update; we're sure that MigrationSettings is already initialized in db 
                // because of logic executed in MigrationStartupTask.cs
                _settingsRepository.UpdateSettingInContext(ctx, settings);
                await ctx.SaveChangesAsync();
            }
            Logs.PayServer.LogInformation($"Full invoice search import successful");
        }

        private async Task<int> CountInvoices()
        {
            using var ctx = _dbContextFactory.CreateContext();
            return await ctx.Invoices.CountAsync();
        }
    }
}
