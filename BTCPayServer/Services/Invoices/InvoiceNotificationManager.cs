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

namespace BTCPayServer.Services.Invoices
{
    public class InvoiceNotificationManager
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
        public InvoiceNotificationManager(
            IBackgroundJobClient jobClient,
            ILogger<InvoiceNotificationManager> logger)
        {
            Logger = logger as ILogger ?? NullLogger.Instance;
            _JobClient = jobClient;
        }

        public void Notify(InvoiceEntity invoice)
        {
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
                var request = new HttpRequestMessage();
                request.Method = HttpMethod.Post;

                var dto = job.Invoice.EntityToDTO();
                InvoicePaymentNotification notification = new InvoicePaymentNotification()
                {
                    Id = dto.Id,
                    Url = dto.Url,
                    BTCDue = dto.BTCDue,
                    BTCPaid = dto.BTCPaid,
                    BTCPrice = dto.BTCPrice,
                    Currency = dto.Currency,
                    CurrentTime = dto.CurrentTime,
                    ExceptionStatus = dto.ExceptionStatus,
                    ExpirationTime = dto.ExpirationTime,
                    InvoiceTime = dto.InvoiceTime,
                    PosData = dto.PosData,
                    Price = dto.Price,
                    Rate = dto.Rate,
                    Status = dto.Status,
                    BuyerFields = job.Invoice.RefundMail == null ? null : new Newtonsoft.Json.Linq.JObject() { new JProperty("buyerEmail", job.Invoice.RefundMail) }
                };
                request.RequestUri = new Uri(job.Invoice.NotificationURL, UriKind.Absolute);
                request.Content = new StringContent(JsonConvert.SerializeObject(notification), Encoding.UTF8, "application/json");
                var response = await _Client.SendAsync(request, cts.Token);
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

        int MaxTry = 6;

        private static string GetHttpJobId(InvoiceEntity invoice)
        {
            return $"{invoice.Id}-{invoice.Status}-HTTP";
        }
    }
}
