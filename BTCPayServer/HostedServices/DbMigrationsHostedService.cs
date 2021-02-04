using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

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

        public DbMigrationsHostedService(InvoiceRepository invoiceRepository, SettingsRepository settingsRepository, ApplicationDbContextFactory dbContextFactory, IOptions<DataDirectories> datadirs)
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

            // Refresh settings since these operations may run for very long time
        }

        private async Task MigratedInvoiceTextSearchToDb(int startFromPage)
        {
            // deleting legacy DBriize database if present
            var dbpath = Path.Combine(_datadirs.Value.DataDir, "InvoiceDB");
            if (Directory.Exists(dbpath))
            {
                Directory.Delete(dbpath, true);
            }

            var invoiceQuery = new InvoiceQuery { IncludeArchived = true };
            var totalCount = await _invoiceRepository.GetInvoicesTotal(invoiceQuery);
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
                        new InvoiceData { Id = invoice.Id, InvoiceSearchData = new List<InvoiceSearchData>() }, 
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
    }
}
