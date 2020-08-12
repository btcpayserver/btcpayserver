using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Hosting;
using NBitpayClient;
using NBXplorer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.HostedServices
{
    public class InvoiceNotificationManager : IHostedService
    {
        readonly HttpClient _Client;

        public class ScheduledJob
        {
            public int TryCount
            {
                get; set;
            }

            public InvoicePaymentNotificationEventWrapper Notification
            {
                get; set;
            }
        }

        readonly IBackgroundJobClient _JobClient;
        readonly EventAggregator _EventAggregator;
        readonly InvoiceRepository _InvoiceRepository;
        private readonly EmailSenderFactory _EmailSenderFactory;
        private readonly StoreRepository _StoreRepository;

        public InvoiceNotificationManager(
            IHttpClientFactory httpClientFactory,
            IBackgroundJobClient jobClient,
            EventAggregator eventAggregator,
            InvoiceRepository invoiceRepository,
            StoreRepository storeRepository,
            EmailSenderFactory emailSenderFactory)
        {
            _Client = httpClientFactory.CreateClient();
            _JobClient = jobClient;
            _EventAggregator = eventAggregator;
            _InvoiceRepository = invoiceRepository;
            _EmailSenderFactory = emailSenderFactory;
            _StoreRepository = storeRepository;
        }

        async Task Notify(InvoiceEntity invoice, InvoiceEvent invoiceEvent, bool extendedNotification)
        {
            var dto = invoice.EntityToDTO();
            var notification = new InvoicePaymentNotificationEventWrapper()
            {
                Data = new InvoicePaymentNotification()
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
                    OrderId = dto.OrderId
                },
                Event = new InvoicePaymentNotificationEvent()
                {
                    Code = (int)invoiceEvent.EventCode,
                    Name = invoiceEvent.Name
                },
                ExtendedNotification = extendedNotification,
                NotificationURL = invoice.NotificationURL?.AbsoluteUri
            };

            // For lightning network payments, paid, confirmed and completed come all at once.
            // So despite the event is "paid" or "confirmed" the Status of the invoice is technically complete
            // This confuse loggers who think their endpoint get duplicated events
            // So here, we just override the status expressed by the notification
            if (invoiceEvent.Name == InvoiceEvent.Confirmed)
            {
                notification.Data.Status = InvoiceState.ToString(InvoiceStatusLegacy.Confirmed);
            }
            if (invoiceEvent.Name == InvoiceEvent.PaidInFull)
            {
                notification.Data.Status = InvoiceState.ToString(InvoiceStatusLegacy.Paid);
            }
            //////////////////

            // We keep backward compatibility with bitpay by passing BTC info to the notification
            // we don't pass other info, as it is a bad idea to use IPN data for logic processing (can be faked)
            var btcCryptoInfo = dto.CryptoInfo.FirstOrDefault(c => c.GetpaymentMethodId() == new PaymentMethodId("BTC", Payments.PaymentTypes.BTCLike) && !string.IsNullOrEmpty(c.Address));
            if (btcCryptoInfo != null)
            {
#pragma warning disable CS0618
                notification.Data.Rate = dto.Rate;
                notification.Data.Url = dto.Url;
                notification.Data.BTCDue = dto.BTCDue;
                notification.Data.BTCPaid = dto.BTCPaid;
                notification.Data.BTCPrice = dto.BTCPrice;
#pragma warning restore CS0618
            }

            if (invoiceEvent.Name != InvoiceEvent.Expired && !String.IsNullOrEmpty(invoice.NotificationEmail))
            {
                var json = NBitcoin.JsonConverters.Serializer.ToString(notification);
                var store = await _StoreRepository.FindStore(invoice.StoreId);
                var storeName = store.StoreName ?? "BTCPay Server";
                var emailBody = $"Store: {storeName}<br>" +
                                $"Invoice ID: {notification.Data.Id}<br>" +
                                $"Status: {notification.Data.Status}<br>" +
                                $"Amount: {notification.Data.Price} {notification.Data.Currency}<br>" +
                                $"<br><details><summary>Details</summary><pre>{json}</pre></details>";

                _EmailSenderFactory.GetEmailSender(invoice.StoreId).SendEmail(
                    invoice.NotificationEmail,
                    $"{storeName} Invoice Notification - ${invoice.StoreId}",
                    emailBody);
            }

            if (invoice.NotificationURL != null)
            {
                var invoiceStr = NBitcoin.JsonConverters.Serializer.ToString(new ScheduledJob() { TryCount = 0, Notification = notification });
                _JobClient.Schedule((cancellation) => NotifyHttp(invoiceStr, cancellation), TimeSpan.Zero);
            }
        }

        public async Task NotifyHttp(string invoiceData, CancellationToken cancellationToken)
        {
            var job = NBitcoin.JsonConverters.Serializer.ToObject<ScheduledJob>(invoiceData);
            bool reschedule = false;
            var aggregatorEvent = new InvoiceIPNEvent(job.Notification.Data.Id, job.Notification.Event.Code, job.Notification.Event.Name);
            try
            {
                HttpResponseMessage response = await SendNotification(job.Notification, cancellationToken);
                reschedule = !response.IsSuccessStatusCode;
                aggregatorEvent.Error = reschedule ? $"Unexpected return code: {(int)response.StatusCode}" : null;
                _EventAggregator.Publish<InvoiceIPNEvent>(aggregatorEvent);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // When the JobClient will be persistent, this will reschedule the job for after reboot
                invoiceData = NBitcoin.JsonConverters.Serializer.ToString(job);
                _JobClient.Schedule((cancellation) => NotifyHttp(invoiceData, cancellation), TimeSpan.FromMinutes(10.0));
                return;
            }
            catch (OperationCanceledException)
            {
                aggregatorEvent.Error = "Timeout";
                _EventAggregator.Publish<InvoiceIPNEvent>(aggregatorEvent);
                reschedule = true;
            }
            catch (Exception ex)
            {
                reschedule = true;

                List<string> messages = new List<string>();
                while (ex != null)
                {
                    messages.Add(ex.Message);
                    ex = ex.InnerException;
                }
                string message = String.Join(',', messages.ToArray());

                aggregatorEvent.Error = $"Unexpected error: {message}";
                _EventAggregator.Publish<InvoiceIPNEvent>(aggregatorEvent);
            }

            job.TryCount++;

            if (job.TryCount < MaxTry && reschedule)
            {
                invoiceData = NBitcoin.JsonConverters.Serializer.ToString(job);
                _JobClient.Schedule((cancellation) => NotifyHttp(invoiceData, cancellation), TimeSpan.FromMinutes(10.0));
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
            [JsonProperty("extendedNotification")]
            public bool ExtendedNotification { get; set; }
            [JsonProperty(PropertyName = "notificationURL")]
            public string NotificationURL { get; set; }
        }

        readonly Encoding UTF8 = new UTF8Encoding(false);
        private async Task<HttpResponseMessage> SendNotification(InvoicePaymentNotificationEventWrapper notification, CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage();
            request.Method = HttpMethod.Post;

            var notificationString = NBitcoin.JsonConverters.Serializer.ToString(notification);
            var jobj = JObject.Parse(notificationString);

            if (notification.ExtendedNotification)
            {
                jobj.Remove("extendedNotification");
                jobj.Remove("notificationURL");
                notificationString = jobj.ToString();
            }
            else
            {
                notificationString = jobj["data"].ToString();
            }

            request.RequestUri = new Uri(notification.NotificationURL, UriKind.Absolute);
            request.Content = new StringContent(notificationString, UTF8, "application/json");
            var response = await Enqueue(notification.Data.Id, async () =>
            {
                using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromMinutes(1.0));
                    return await _Client.SendAsync(request, cts.Token);
                }
            });
            return response;
        }

        readonly Dictionary<string, Task> _SendingRequestsByInvoiceId = new Dictionary<string, Task>();


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

        readonly int MaxTry = 6;
        readonly CompositeDisposable leases = new CompositeDisposable();
        public Task StartAsync(CancellationToken cancellationToken)
        {
            leases.Add(_EventAggregator.Subscribe<InvoiceEvent>(async e =>
            {
                var invoice = await _InvoiceRepository.GetInvoice(e.Invoice.Id);
                if (invoice == null)
                    return;
                // we need to use the status in the event and not in the invoice. The invoice might now be in another status.
                if (invoice.FullNotifications)
                {
                    if (e.Name == InvoiceEvent.Expired ||
                       e.Name == InvoiceEvent.PaidInFull ||
                       e.Name == InvoiceEvent.FailedToConfirm ||
                       e.Name == InvoiceEvent.MarkedInvalid ||
                       e.Name == InvoiceEvent.MarkedCompleted ||
                       e.Name == InvoiceEvent.FailedToConfirm ||
                       e.Name == InvoiceEvent.Completed ||
                       e.Name == InvoiceEvent.ExpiredPaidPartial
                     )
                        _ = Notify(invoice, e, false);
                }

                if (e.Name == InvoiceEvent.Confirmed)
                {
                    _ = Notify(invoice, e, false);
                }

                if (invoice.ExtendedNotifications)
                {
                    _ = Notify(invoice, e, true);
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
