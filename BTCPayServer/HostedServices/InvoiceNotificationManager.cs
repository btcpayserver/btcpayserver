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
using BTCPayServer.Services.Invoices;
using BTCPayServer.Payments;
using BTCPayServer.Services.Mails;

namespace BTCPayServer.HostedServices
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

            public int? EventCode { get; set; }
            public string Message { get; set; }
        }

        public ILogger Logger
        {
            get; set;
        }

        IBackgroundJobClient _JobClient;
        EventAggregator _EventAggregator;
        InvoiceRepository _InvoiceRepository;
        BTCPayNetworkProvider _NetworkProvider;
        IEmailSender _EmailSender;

        public InvoiceNotificationManager(
            IBackgroundJobClient jobClient,
            EventAggregator eventAggregator,
            InvoiceRepository invoiceRepository,
            BTCPayNetworkProvider networkProvider,
            ILogger<InvoiceNotificationManager> logger,
            IEmailSender emailSender)
        {
            Logger = logger as ILogger ?? NullLogger.Instance;
            _JobClient = jobClient;
            _EventAggregator = eventAggregator;
            _InvoiceRepository = invoiceRepository;
            _NetworkProvider = networkProvider;
            _EmailSender = emailSender;
        }

        async Task Notify(InvoiceEntity invoice, int? eventCode = null, string name = null)
        {
            CancellationTokenSource cts = new CancellationTokenSource(10000);

            if (!String.IsNullOrEmpty(invoice.NotificationEmail))
            {
                // just extracting most important data for email body, merchant should query API back for full invoice based on Invoice.Id
                var ipn = new
                {
                    invoice.Id,
                    invoice.Status,
                    invoice.StoreId
                };
                // TODO: Consider adding info on ItemDesc and payment info (amount)

                var emailBody = NBitcoin.JsonConverters.Serializer.ToString(ipn);
                await _EmailSender.SendEmailAsync(
                    invoice.NotificationEmail, $"BtcPayServer Invoice Notification - ${invoice.StoreId}", emailBody);
            }

            try
            {
                if (string.IsNullOrEmpty(invoice.NotificationURL))
                    return;
                _EventAggregator.Publish<InvoiceIPNEvent>(new InvoiceIPNEvent(invoice.Id, eventCode, name));
                var response = await SendNotification(invoice, eventCode, name, cts.Token);
                response.EnsureSuccessStatusCode();
                return;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                _EventAggregator.Publish<InvoiceIPNEvent>(new InvoiceIPNEvent(invoice.Id, eventCode, name)
                {
                    Error = "Timeout"
                });
            }
            catch (Exception ex) // It fails, it is OK because we try with hangfire after
            {
                _EventAggregator.Publish<InvoiceIPNEvent>(new InvoiceIPNEvent(invoice.Id, eventCode, name)
                {
                    Error = ex.Message
                });
            }
            var invoiceStr = NBitcoin.JsonConverters.Serializer.ToString(new ScheduledJob() { TryCount = 0, Invoice = invoice, EventCode = eventCode, Message = name });
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
                HttpResponseMessage response = await SendNotification(job.Invoice, job.EventCode, job.Message, cts.Token);
                reschedule = !response.IsSuccessStatusCode;
                Logger.LogInformation("Job " + jobId + " returned " + response.StatusCode);

                _EventAggregator.Publish<InvoiceIPNEvent>(new InvoiceIPNEvent(job.Invoice.Id, job.EventCode, job.Message)
                {
                    Error = reschedule ? $"Unexpected return code: {(int)response.StatusCode}" : null
                });
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                _EventAggregator.Publish<InvoiceIPNEvent>(new InvoiceIPNEvent(job.Invoice.Id, job.EventCode, job.Message)
                {
                    Error = "Timeout"
                });
                reschedule = true;
                Logger.LogInformation("Job " + jobId + " timed out");
            }
            catch (Exception ex) // It fails, it is OK because we try with hangfire after
            {
                _EventAggregator.Publish<InvoiceIPNEvent>(new InvoiceIPNEvent(job.Invoice.Id, job.EventCode, job.Message)
                {
                    Error = ex.Message
                });
                reschedule = true;

                List<string> messages = new List<string>();
                while (ex != null)
                {
                    messages.Add(ex.Message);
                    ex = ex.InnerException;
                }
                string message = String.Join(',', messages.ToArray());
                Logger.LogInformation("Job " + jobId + " threw exception " + message);

                _EventAggregator.Publish<InvoiceIPNEvent>(new InvoiceIPNEvent(job.Invoice.Id, job.EventCode, job.Message)
                {
                    Error = $"Unexpected error: {message}"
                });
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

        public class InvoicePaymentNotificationEvent
        {
            [JsonProperty("code")]
            public int Code { get; set; }
            [JsonProperty("name")]
            public string Name { get; set; }
        }
        public class InvoicePaymentNotificationEventWrapper
        {
            [JsonProperty("event")]
            public InvoicePaymentNotificationEvent Event { get; set; }
            [JsonProperty("data")]
            public InvoicePaymentNotification Data { get; set; }
        }

        Encoding UTF8 = new UTF8Encoding(false);
        private async Task<HttpResponseMessage> SendNotification(InvoiceEntity invoice, int? eventCode, string name, CancellationToken cancellation)
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
                BuyerFields = invoice.RefundMail == null ? null : new Newtonsoft.Json.Linq.JObject() { new JProperty("buyerEmail", invoice.RefundMail) },
                PaymentSubtotals = dto.PaymentSubtotals,
                PaymentTotals = dto.PaymentTotals,
                AmountPaid = dto.AmountPaid,
                ExchangeRates = dto.ExchangeRates,

            };

            // We keep backward compatibility with bitpay by passing BTC info to the notification
            // we don't pass other info, as it is a bad idea to use IPN data for logic processing (can be faked)
            var btcCryptoInfo = dto.CryptoInfo.FirstOrDefault(c => c.GetpaymentMethodId() == new PaymentMethodId("BTC", Payments.PaymentTypes.BTCLike));
            if (btcCryptoInfo != null)
            {
#pragma warning disable CS0618
                notification.Rate = dto.Rate;
                notification.Url = dto.Url;
                notification.BTCDue = dto.BTCDue;
                notification.BTCPaid = dto.BTCPaid;
                notification.BTCPrice = dto.BTCPrice;
#pragma warning restore CS0618
            }

            string notificationString = null;
            if (eventCode.HasValue)
            {
                var wrapper = new InvoicePaymentNotificationEventWrapper();
                wrapper.Data = notification;
                wrapper.Event = new InvoicePaymentNotificationEvent() { Code = eventCode.Value, Name = name };
                notificationString = JsonConvert.SerializeObject(wrapper);
            }
            else
            {
                notificationString = JsonConvert.SerializeObject(notification);
            }

            request.RequestUri = new Uri(invoice.NotificationURL, UriKind.Absolute);
            request.Content = new StringContent(notificationString, UTF8, "application/json");
            var response = await Enqueue(invoice.Id, async () => await _Client.SendAsync(request, cancellation));
            return response;
        }

        Dictionary<string, Task> _SendingRequestsByInvoiceId = new Dictionary<string, Task>();


        /// <summary>
        /// Will make sure only one callback is called at once on the same invoiceId
        /// </summary>
        /// <param name="id"></param>
        /// <param name="sendRequest"></param>
        /// <returns></returns>
        private async Task<T> Enqueue<T>(string id, Func<Task<T>> sendRequest)
        {
            Task<T> sending = null;
            lock (_SendingRequestsByInvoiceId)
            {
                if (_SendingRequestsByInvoiceId.TryGetValue(id, out var executing))
                {
                    var completion = new TaskCompletionSource<T>();
                    sending = completion.Task;
                    _SendingRequestsByInvoiceId.Remove(id);
                    _SendingRequestsByInvoiceId.Add(id, sending);
                    executing.ContinueWith(_ =>
                    {
                        sendRequest()
                            .ContinueWith(t =>
                            {
                                if (t.Status == TaskStatus.RanToCompletion)
                                {
                                    completion.TrySetResult(t.Result);
                                }
                                if (t.Status == TaskStatus.Faulted)
                                {
                                    completion.TrySetException(t.Exception);
                                }
                                if (t.Status == TaskStatus.Canceled)
                                {
                                    completion.TrySetCanceled();
                                }
                            }, TaskScheduler.Default);
                    }, TaskScheduler.Default);
                }
                else
                {
                    sending = sendRequest();
                    _SendingRequestsByInvoiceId.Add(id, sending);
                }
                sending.ContinueWith(o =>
                {
                    lock (_SendingRequestsByInvoiceId)
                    {
                        _SendingRequestsByInvoiceId.TryGetValue(id, out var executing2);
                        if (executing2 == sending)
                            _SendingRequestsByInvoiceId.Remove(id);
                    }
                }, TaskScheduler.Default);
            }
            return await sending;
        }

        int MaxTry = 6;

        private static string GetHttpJobId(InvoiceEntity invoice)
        {
            return $"{invoice.Id}-{invoice.Status}-HTTP";
        }

        CompositeDisposable leases = new CompositeDisposable();
        public Task StartAsync(CancellationToken cancellationToken)
        {
            leases.Add(_EventAggregator.Subscribe<InvoiceEvent>(async e =>
            {
                var invoice = await _InvoiceRepository.GetInvoice(null, e.Invoice.Id);
                if (invoice == null)
                    return;
                List<Task> tasks = new List<Task>();

                // Awaiting this later help make sure invoices should arrive in order
                tasks.Add(SaveEvent(invoice.Id, e));

                // we need to use the status in the event and not in the invoice. The invoice might now be in another status.
                if (invoice.FullNotifications)
                {
                    if (e.Name == "invoice_expired" ||
                       e.Name == "invoice_paidInFull" ||
                       e.Name == "invoice_failedToConfirm" ||
                       e.Name == "invoice_markedInvalid" ||
                       e.Name == "invoice_failedToConfirm" ||
                       e.Name == "invoice_completed" ||
                       e.Name == "invoice_expiredPaidPartial"
                     )
                        tasks.Add(Notify(invoice));
                }

                if (e.Name == "invoice_confirmed")
                {
                    tasks.Add(Notify(invoice));
                }

                if (invoice.ExtendedNotifications)
                {
                    tasks.Add(Notify(invoice, e.EventCode, e.Name));
                }
                await Task.WhenAll(tasks.ToArray());
            }));


            leases.Add(_EventAggregator.Subscribe<InvoiceDataChangedEvent>(async e =>
            {
                await SaveEvent(e.InvoiceId, e);
            }));


            leases.Add(_EventAggregator.Subscribe<InvoiceStopWatchedEvent>(async e =>
            {
                await SaveEvent(e.InvoiceId, e);
            }));

            leases.Add(_EventAggregator.Subscribe<InvoiceIPNEvent>(async e =>
            {
                await SaveEvent(e.InvoiceId, e);
            }));

            return Task.CompletedTask;
        }

        private Task SaveEvent(string invoiceId, object evt)
        {
            return _InvoiceRepository.AddInvoiceEvent(invoiceId, evt);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            leases.Dispose();
            return Task.CompletedTask;
        }
    }
}
