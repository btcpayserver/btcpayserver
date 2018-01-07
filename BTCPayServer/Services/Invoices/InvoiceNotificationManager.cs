using Hangfire;
using Hangfire.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hangfire.Annotations;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http;
using System.Text;
using System.Threading;
using NBitpayClient;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using BTCPayServer.Events;
using NBXplorer;

namespace BTCPayServer.Services.Invoices
{
    public class InvoiceNotificationManager : IHostedService
    {
        public static HttpClient _Client = new HttpClient();

        public class ScheduledJob
        {
            public int TryCount
            {
                get; set;
            }

            public InvoiceEntity Invoice
            {
                get; set;
            }
        }

        public ILogger Logger
        {
            get; set;
        }

        IBackgroundJobClient _JobClient;
        EventAggregator _EventAggregator;
        InvoiceRepository _InvoiceRepository;
        BTCPayNetworkProvider _NetworkProvider;

        public InvoiceNotificationManager(
            IBackgroundJobClient jobClient,
            EventAggregator eventAggregator,
            InvoiceRepository invoiceRepository,
            BTCPayNetworkProvider networkProvider,
            ILogger<InvoiceNotificationManager> logger)
        {
            Logger = logger as ILogger ?? NullLogger.Instance;
            _JobClient = jobClient;
            _EventAggregator = eventAggregator;
            _InvoiceRepository = invoiceRepository;
            _NetworkProvider = networkProvider;
        }

        async Task Notify(InvoiceEntity invoice)
        {
            CancellationTokenSource cts = new CancellationTokenSource(10000);
            try
            {
                await SendNotification(invoice, cts.Token);
                return;
            }
            catch // It fails, it is OK because we try with hangfire after
            {
            }
            var invoiceStr = NBitcoin.JsonConverters.Serializer.ToString(new ScheduledJob() { TryCount = 0, Invoice = invoice });
            if (!string.IsNullOrEmpty(invoice.NotificationURL))
                _JobClient.Schedule(() => NotifyHttp(invoiceStr), TimeSpan.Zero);
        }

        ConcurrentDictionary<string, string> _Executing = new ConcurrentDictionary<string, string>();
        public async Task NotifyHttp(string invoiceData)
        {
            var job = NBitcoin.JsonConverters.Serializer.ToObject<ScheduledJob>(invoiceData);
            var jobId = GetHttpJobId(job.Invoice);

            if (!_Executing.TryAdd(jobId, jobId))
                return; //For some reason, Hangfire fire the job several time

            Logger.LogInformation("Running " + jobId);
            bool reschedule = false;
            CancellationTokenSource cts = new CancellationTokenSource(10000);
            try
            {
                HttpResponseMessage response = await SendNotification(job.Invoice, cts.Token);
                reschedule = response.StatusCode != System.Net.HttpStatusCode.OK;
                Logger.LogInformation("Job " + jobId + " returned " + response.StatusCode);
            }
            catch (Exception ex)
            {
                reschedule = true;
                Logger.LogInformation("Job " + jobId + " threw exception " + ex.Message);
            }
            finally { cts.Dispose(); _Executing.TryRemove(jobId, out jobId); }

            job.TryCount++;

            if (job.TryCount < MaxTry && reschedule)
            {
                Logger.LogInformation("Rescheduling " + jobId + " in 10 minutes, remaining try " + (MaxTry - job.TryCount));

                invoiceData = NBitcoin.JsonConverters.Serializer.ToString(job);
                _JobClient.Schedule(() => NotifyHttp(invoiceData), TimeSpan.FromMinutes(10.0));
            }
        }

        private async Task<HttpResponseMessage> SendNotification(InvoiceEntity invoice, CancellationToken cancellation)
        {
            var request = new HttpRequestMessage();
            request.Method = HttpMethod.Post;

            var dto = invoice.EntityToDTO(_NetworkProvider);
            InvoicePaymentNotification notification = new InvoicePaymentNotification()
            {
                Id = dto.Id,
                Currency = dto.Currency,
                CurrentTime = dto.CurrentTime,
                ExceptionStatus = dto.ExceptionStatus,
                ExpirationTime = dto.ExpirationTime,
                InvoiceTime = dto.InvoiceTime,
                PosData = dto.PosData,
                Price = dto.Price,
                Status = dto.Status,
                BuyerFields = invoice.RefundMail == null ? null : new Newtonsoft.Json.Linq.JObject() { new JProperty("buyerEmail", invoice.RefundMail) }
            };

            // We keep backward compatibility with bitpay by passing BTC info to the notification
            // we don't pass other info, as it is a bad idea to use IPN data for logic processing (can be faked)
            var btcCryptoInfo = dto.CryptoInfo.FirstOrDefault(c => c.CryptoCode == "BTC");
            if(btcCryptoInfo != null)
            {
#pragma warning disable CS0618
                notification.Rate = (double)dto.Rate;
                notification.Url = dto.Url;
                notification.BTCDue = dto.BTCDue;
                notification.BTCPaid = dto.BTCPaid;
                notification.BTCPrice = dto.BTCPrice;
#pragma warning restore CS0618
            }
            request.RequestUri = new Uri(invoice.NotificationURL, UriKind.Absolute);
            request.Content = new StringContent(JsonConvert.SerializeObject(notification), Encoding.UTF8, "application/json");
            var response = await _Client.SendAsync(request, cancellation);
            return response;
        }

        int MaxTry = 6;

        private static string GetHttpJobId(InvoiceEntity invoice)
        {
            return $"{invoice.Id}-{invoice.Status}-HTTP";
        }

        CompositeDisposable leases = new CompositeDisposable();
        public Task StartAsync(CancellationToken cancellationToken)
        {
            leases.Add(_EventAggregator.Subscribe<InvoiceStatusChangedEvent>(async e => 
            {
                var invoice = await _InvoiceRepository.GetInvoice(null, e.InvoiceId);

                // we need to use the status in the event and not in the invoice. The invoice might now be in another status.
                if (invoice.FullNotifications)
                {
                    if (e.NewState == "expired" ||
                       e.NewState == "paid" ||
                       e.NewState == "invalid" ||
                       e.NewState == "complete"
                     )
                        await Notify(invoice);
                }
                
                if(e.NewState == "confirmed")
                {
                    await Notify(invoice);
                }
            }));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            leases.Dispose();
            return Task.CompletedTask;
        }
    }
}
