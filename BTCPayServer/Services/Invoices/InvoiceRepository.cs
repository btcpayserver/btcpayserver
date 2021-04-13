using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using Encoders = NBitcoin.DataEncoders.Encoders;
using InvoiceData = BTCPayServer.Data.InvoiceData;

namespace BTCPayServer.Services.Invoices
{
    public class InvoiceRepository
    {
        static JsonSerializerSettings DefaultSerializerSettings;
        static InvoiceRepository()
        {
            DefaultSerializerSettings = new JsonSerializerSettings();
            NBitcoin.JsonConverters.Serializer.RegisterFrontConverters(DefaultSerializerSettings);
        }

        private readonly ApplicationDbContextFactory _ContextFactory;
        private readonly EventAggregator _eventAggregator;
        private readonly BTCPayNetworkProvider _Networks;

        public InvoiceRepository(ApplicationDbContextFactory contextFactory,
            BTCPayNetworkProvider networks, EventAggregator eventAggregator)
        {
            _ContextFactory = contextFactory;
            _Networks = networks;
            _eventAggregator = eventAggregator;
        }

        public async Task<Data.WebhookDeliveryData> GetWebhookDelivery(string invoiceId, string deliveryId)
        {
            using var ctx = _ContextFactory.CreateContext();
            return await ctx.InvoiceWebhookDeliveries
                .Where(d => d.InvoiceId == invoiceId && d.DeliveryId == deliveryId)
                .Select(d => d.Delivery)
                .FirstOrDefaultAsync();
        }

        public InvoiceEntity CreateNewInvoice()
        {
            return new InvoiceEntity()
            {
                Networks = _Networks,
                Version = InvoiceEntity.Lastest_Version,
                InvoiceTime = DateTimeOffset.UtcNow,
                Metadata = new InvoiceMetadata()
            };
        }

        public async Task<bool> RemovePendingInvoice(string invoiceId)
        {
            Logs.PayServer.LogInformation($"Remove pending invoice {invoiceId}");
            using (var ctx = _ContextFactory.CreateContext())
            {
                ctx.PendingInvoices.Remove(new PendingInvoiceData() { Id = invoiceId });
                try
                {
                    await ctx.SaveChangesAsync();
                    return true;
                }
                catch (DbUpdateException) { return false; }
            }
        }

        public async Task<IEnumerable<InvoiceEntity>> GetInvoicesFromAddresses(string[] addresses)
        {
            using (var db = _ContextFactory.CreateContext())
            {
                return (await db.AddressInvoices
                    .Include(a => a.InvoiceData.Payments)
#pragma warning disable CS0618
                    .Where(a => addresses.Contains(a.Address))
#pragma warning restore CS0618
                    .Select(a => a.InvoiceData)
                    .ToListAsync()).Select(ToEntity);
            }
        }

        public async Task<string[]> GetPendingInvoices()
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                return await ctx.PendingInvoices.AsQueryable().Select(data => data.Id).ToArrayAsync();
            }
        }

        public async Task<List<Data.WebhookDeliveryData>> GetWebhookDeliveries(string invoiceId)
        {
            using var ctx = _ContextFactory.CreateContext();
            return await ctx.InvoiceWebhookDeliveries
                .Where(s => s.InvoiceId == invoiceId)
                .Select(s => s.Delivery)
                .OrderByDescending(s => s.Timestamp)
                .ToListAsync();
        }

        public async Task<AppData[]> GetAppsTaggingStore(string storeId)
        {
            if (storeId == null)
                throw new ArgumentNullException(nameof(storeId));
            using (var ctx = _ContextFactory.CreateContext())
            {
                return await ctx.Apps.Where(a => a.StoreDataId == storeId && a.TagAllInvoices).ToArrayAsync();
            }
        }

        public async Task UpdateInvoice(string invoiceId, UpdateCustomerModel data)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                var invoiceData = await ctx.Invoices.FindAsync(invoiceId).ConfigureAwait(false);
                if (invoiceData == null)
                    return;
                if (invoiceData.CustomerEmail == null && data.Email != null)
                {
                    invoiceData.CustomerEmail = data.Email;
                    AddToTextSearch(ctx, invoiceData, invoiceData.CustomerEmail);
                }
                await ctx.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task ExtendInvoiceMonitor(string invoiceId)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                var invoiceData = await ctx.Invoices.FindAsync(invoiceId);

                var invoice = invoiceData.GetBlob(_Networks);
                invoice.MonitoringExpiration = invoice.MonitoringExpiration.AddHours(1);
                invoiceData.Blob = ToBytes(invoice, null);

                await ctx.SaveChangesAsync();
            }
        }

        public async Task<InvoiceEntity> CreateInvoiceAsync(string storeId, InvoiceEntity invoice)
        {
            var textSearch = new List<string>();
            invoice = Clone(invoice);
            invoice.Networks = _Networks;
            invoice.Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16));
#pragma warning disable CS0618
            invoice.Payments = new List<PaymentEntity>();
#pragma warning restore CS0618
            invoice.StoreId = storeId;
            using (var context = _ContextFactory.CreateContext())
            {
                var invoiceData = new Data.InvoiceData()
                {
                    StoreDataId = storeId,
                    Id = invoice.Id,
                    Created = invoice.InvoiceTime,
                    Blob = ToBytes(invoice, null),
                    OrderId = invoice.Metadata.OrderId,
#pragma warning disable CS0618 // Type or member is obsolete
                    Status = invoice.StatusString,
#pragma warning restore CS0618 // Type or member is obsolete
                    ItemCode = invoice.Metadata.ItemCode,
                    CustomerEmail = invoice.RefundMail,
                    Archived = false
                };
                await context.Invoices.AddAsync(invoiceData);


                foreach (var paymentMethod in invoice.GetPaymentMethods())
                {
                    if (paymentMethod.Network == null)
                        throw new InvalidOperationException("CryptoCode unsupported");
                    var details = paymentMethod.GetPaymentMethodDetails();
                    if (!details.Activated)
                    {
                        continue;
                    }
                    var paymentDestination = details.GetPaymentDestination();
                    string address = GetDestination(paymentMethod);
                    await context.AddressInvoices.AddAsync(new AddressInvoiceData()
                    {
                        InvoiceDataId = invoice.Id,
                        CreatedTime = DateTimeOffset.UtcNow,
                    }.Set(address, paymentMethod.GetId()));

                    await context.HistoricalAddressInvoices.AddAsync(new HistoricalAddressInvoiceData()
                    {
                        InvoiceDataId = invoice.Id,
                        Assigned = DateTimeOffset.UtcNow
                    }.SetAddress(paymentDestination, paymentMethod.GetId().ToString()));
                    textSearch.Add(paymentDestination);
                    textSearch.Add(paymentMethod.Calculate().TotalDue.ToString());
                }
                await context.PendingInvoices.AddAsync(new PendingInvoiceData() { Id = invoice.Id });

                textSearch.Add(invoice.Id);
                textSearch.Add(invoice.InvoiceTime.ToString(CultureInfo.InvariantCulture));
                textSearch.Add(invoice.Price.ToString(CultureInfo.InvariantCulture));
                textSearch.Add(invoice.Metadata.OrderId);
                textSearch.Add(invoice.StoreId);
                textSearch.Add(invoice.Metadata.BuyerEmail);
                AddToTextSearch(context, invoiceData, textSearch.ToArray());

                await context.SaveChangesAsync().ConfigureAwait(false);
            }


            return invoice;
        }

        private InvoiceEntity Clone(InvoiceEntity invoice)
        {
            var temp = new InvoiceData();
            temp.Blob = ToBytes(invoice);
            return temp.GetBlob(_Networks);
        }

        public async Task AddInvoiceLogs(string invoiceId, InvoiceLogs logs)
        {
            await using var context = _ContextFactory.CreateContext();
            foreach (var log in logs.ToList())
            {
                await context.InvoiceEvents.AddAsync(new InvoiceEventData()
                {
                    Severity = log.Severity,
                    InvoiceDataId = invoiceId,
                    Message = log.Log,
                    Timestamp = log.Timestamp,
                    UniqueId = Encoders.Hex.EncodeData(RandomUtils.GetBytes(10))
                });
            }
            await context.SaveChangesAsync().ConfigureAwait(false);
        }

        private string GetDestination(PaymentMethod paymentMethod)
        {
            // For legacy reason, BitcoinLikeOnChain is putting the hashes of addresses in database
            if (paymentMethod.GetId().PaymentType == Payments.PaymentTypes.BTCLike)
            {
                var network = (BTCPayNetwork)paymentMethod.Network;
                var details =
                    (Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod)paymentMethod.GetPaymentMethodDetails();
                if (!details.Activated)
                {
                    return null;
                }
                return details.GetDepositAddress(network.NBitcoinNetwork).ScriptPubKey.Hash.ToString();
            }
            ///////////////
            return paymentMethod.GetPaymentMethodDetails().GetPaymentDestination();
        }

        public async Task<bool> NewPaymentDetails(string invoiceId, IPaymentMethodDetails paymentMethodDetails, BTCPayNetworkBase network)
        {
            await using var context = _ContextFactory.CreateContext();
            var invoice = (await context.Invoices.Where(i => i.Id == invoiceId).ToListAsync()).FirstOrDefault();
            if (invoice == null)
                return false;

            var invoiceEntity = invoice.GetBlob(_Networks);
            var paymentMethod = invoiceEntity.GetPaymentMethod(network, paymentMethodDetails.GetPaymentType());
            if (paymentMethod == null)
                return false;

            var existingPaymentMethod = paymentMethod.GetPaymentMethodDetails();
            if (existingPaymentMethod.GetPaymentDestination() != null)
            {
                MarkUnassigned(invoiceId, context, paymentMethod.GetId());
            }
            paymentMethod.SetPaymentMethodDetails(paymentMethodDetails);
#pragma warning disable CS0618
            if (network.IsBTC)
            {
                invoiceEntity.DepositAddress = paymentMethod.DepositAddress;
            }
#pragma warning restore CS0618
            invoiceEntity.SetPaymentMethod(paymentMethod);
            invoice.Blob = ToBytes(invoiceEntity, network);

            await context.AddressInvoices.AddAsync(new AddressInvoiceData()
            {
                InvoiceDataId = invoiceId,
                CreatedTime = DateTimeOffset.UtcNow
            }
                .Set(GetDestination(paymentMethod), paymentMethod.GetId()));
            await context.HistoricalAddressInvoices.AddAsync(new HistoricalAddressInvoiceData()
            {
                InvoiceDataId = invoiceId,
                Assigned = DateTimeOffset.UtcNow
            }.SetAddress(paymentMethodDetails.GetPaymentDestination(), network.CryptoCode));

            AddToTextSearch(context, invoice, paymentMethodDetails.GetPaymentDestination());
            await context.SaveChangesAsync();
            return true;
        }

        public async Task UpdateInvoicePaymentMethod(string invoiceId, PaymentMethod paymentMethod)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var invoice = await context.Invoices.FindAsync(invoiceId);
                if (invoice == null)
                    return;
                var network = paymentMethod.Network;
                var invoiceEntity = invoice.GetBlob(_Networks);
                var newDetails = paymentMethod.GetPaymentMethodDetails();
                var existing = invoiceEntity.GetPaymentMethod(paymentMethod.GetId());
                if (existing.GetPaymentMethodDetails().GetPaymentDestination() != newDetails.GetPaymentDestination() && newDetails.Activated)
                {
                    await context.AddressInvoices.AddAsync(new AddressInvoiceData()
                        {
                            InvoiceDataId = invoiceId,
                            CreatedTime = DateTimeOffset.UtcNow
                        }
                        .Set(GetDestination(paymentMethod), paymentMethod.GetId()));
                    await context.HistoricalAddressInvoices.AddAsync(new HistoricalAddressInvoiceData()
                    {
                        InvoiceDataId = invoiceId,
                        Assigned = DateTimeOffset.UtcNow
                    }.SetAddress(paymentMethod.GetPaymentMethodDetails().GetPaymentDestination(), network.CryptoCode));
                }
                invoiceEntity.SetPaymentMethod(paymentMethod);
                invoice.Blob = ToBytes(invoiceEntity, network);
                AddToTextSearch(context, invoice, paymentMethod.GetPaymentMethodDetails().GetPaymentDestination());
                await context.SaveChangesAsync();
                
            }
        }

        public async Task AddPendingInvoiceIfNotPresent(string invoiceId)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                if (!context.PendingInvoices.Any(a => a.Id == invoiceId))
                {
                    context.PendingInvoices.Add(new PendingInvoiceData() { Id = invoiceId });
                    try
                    {
                        await context.SaveChangesAsync();
                    }
                    catch (DbUpdateException) { } // Already exists
                }
            }
        }

        public async Task AddInvoiceEvent(string invoiceId, object evt, InvoiceEventData.EventSeverity severity)
        {
            await using var context = _ContextFactory.CreateContext();
            await context.InvoiceEvents.AddAsync(new InvoiceEventData()
            {
                Severity = severity,
                InvoiceDataId = invoiceId,
                Message = evt.ToString(),
                Timestamp = DateTimeOffset.UtcNow,
                UniqueId = Encoders.Hex.EncodeData(RandomUtils.GetBytes(10))
            });
            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateException) { } // Probably the invoice does not exists anymore
        }

        private static void MarkUnassigned(string invoiceId, ApplicationDbContext context,
            PaymentMethodId paymentMethodId)
        {
            var paymentMethodIdStr = paymentMethodId?.ToString();
            var addresses = context.HistoricalAddressInvoices.Where(data =>
                (data.InvoiceDataId == invoiceId && paymentMethodIdStr == null ||
#pragma warning disable CS0618 // Type or member is obsolete
                 data.CryptoCode == paymentMethodIdStr) &&
#pragma warning restore CS0618 // Type or member is obsolete
                data.UnAssigned == null);
            foreach (var historicalAddressInvoiceData in addresses)
            {
                historicalAddressInvoiceData.UnAssigned = DateTimeOffset.UtcNow;
            }
        }

        public async Task UnaffectAddress(string invoiceId)
        {
            await using var context = _ContextFactory.CreateContext();
            MarkUnassigned(invoiceId, context, null);
            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateException) { } //Possibly, it was unassigned before
        }

        public static void AddToTextSearch(ApplicationDbContext context, InvoiceData invoice, params string[] terms)
        {
            var filteredTerms = terms.Where(t => !string.IsNullOrWhiteSpace(t)
                && (invoice.InvoiceSearchData == null || invoice.InvoiceSearchData.All(data => data.Value != t)))
                .Distinct()
                .Select(s => new InvoiceSearchData() { InvoiceDataId = invoice.Id, Value = s });
            context.AddRange(filteredTerms);
        }

        public async Task UpdateInvoiceStatus(string invoiceId, InvoiceState invoiceState)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var invoiceData = await context.FindAsync<Data.InvoiceData>(invoiceId).ConfigureAwait(false);
                if (invoiceData == null)
                    return;
                invoiceData.Status = InvoiceState.ToString(invoiceState.Status);
                invoiceData.ExceptionStatus = InvoiceState.ToString(invoiceState.ExceptionStatus);
                await context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task MassArchive(string[] invoiceIds)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var items = context.Invoices.Where(a => invoiceIds.Contains(a.Id));
                if (items == null)
                {
                    return;
                }

                foreach (InvoiceData invoice in items)
                {
                    invoice.Archived = true;
                }

                await context.SaveChangesAsync();
            }
        }

        public async Task ToggleInvoiceArchival(string invoiceId, bool archived, string storeId = null)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var invoiceData = await context.FindAsync<InvoiceData>(invoiceId).ConfigureAwait(false);
                if (invoiceData == null || invoiceData.Archived == archived ||
                    (storeId != null &&
                     !invoiceData.StoreDataId.Equals(storeId, StringComparison.InvariantCultureIgnoreCase)))
                    return;
                invoiceData.Archived = archived;
                await context.SaveChangesAsync().ConfigureAwait(false);
            }
        }
        public async Task<InvoiceEntity> UpdateInvoiceMetadata(string invoiceId, string storeId, JObject metadata)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var invoiceData = await GetInvoiceRaw(invoiceId, context);
                if (invoiceData == null || (storeId != null &&
                                            !invoiceData.StoreDataId.Equals(storeId,
                                                StringComparison.InvariantCultureIgnoreCase)))
                    return null;
                var blob = invoiceData.GetBlob(_Networks);
                blob.Metadata = InvoiceMetadata.FromJObject(metadata);
                invoiceData.Blob = ToBytes(blob);
                await context.SaveChangesAsync().ConfigureAwait(false);
                return ToEntity(invoiceData);
            }
        }
        public async Task<bool> MarkInvoiceStatus(string invoiceId, InvoiceStatus status)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var invoiceData = await GetInvoiceRaw(invoiceId, context);
                if (invoiceData == null)
                {
                    return false;
                }

                context.Attach(invoiceData);
                string eventName;
                string legacyStatus;
                switch (status)
                {
                    case InvoiceStatus.Settled:
                        if (!invoiceData.GetInvoiceState().CanMarkComplete())
                        {
                            return false;
                        }

                        eventName = InvoiceEvent.MarkedCompleted;
                        legacyStatus = InvoiceStatusLegacy.Complete.ToString();
                        break;
                    case InvoiceStatus.Invalid:
                        if (!invoiceData.GetInvoiceState().CanMarkInvalid())
                        {
                            return false;
                        }
                        eventName = InvoiceEvent.MarkedInvalid;
                        legacyStatus = InvoiceStatusLegacy.Invalid.ToString();
                        break;
                    default:
                        return false;
                }

                invoiceData.Status = legacyStatus.ToLowerInvariant();
                invoiceData.ExceptionStatus = InvoiceExceptionStatus.Marked.ToString().ToLowerInvariant();
                _eventAggregator.Publish(new InvoiceEvent(ToEntity(invoiceData), eventName));
                await context.SaveChangesAsync();
            }

            return true;
        }

        public async Task<InvoiceEntity> GetInvoice(string id, bool inludeAddressData = false)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var res = await GetInvoiceRaw(id, context, inludeAddressData);
                return res == null ? null : ToEntity(res);
            }            
        }
        public async Task<InvoiceEntity[]> GetInvoices(string[] invoiceIds)
        {
            var invoiceIdSet = invoiceIds.ToHashSet();
            using (var context = _ContextFactory.CreateContext())
            {
                IQueryable<Data.InvoiceData> query =
                    context
                    .Invoices
                    .Include(o => o.Payments)
                    .Where(o => invoiceIdSet.Contains(o.Id));

                return (await query.ToListAsync()).Select(o => ToEntity(o)).ToArray();
            }
        }

        private async Task<InvoiceData> GetInvoiceRaw(string id, ApplicationDbContext dbContext, bool inludeAddressData = false)
        {
            IQueryable<Data.InvoiceData> query =
                    dbContext
                    .Invoices
                    .Include(o => o.Payments);
            if (inludeAddressData)
                query = query.Include(o => o.HistoricalAddressInvoices).Include(o => o.AddressInvoices);
            query = query.Where(i => i.Id == id);

            var invoice = (await query.ToListAsync()).FirstOrDefault();
            if (invoice == null)
                return null;

            return invoice;
        }

        private InvoiceEntity ToEntity(Data.InvoiceData invoice)
        {
            var entity = invoice.GetBlob(_Networks);
            PaymentMethodDictionary paymentMethods = null;
#pragma warning disable CS0618
            entity.Payments = invoice.Payments.Select(p =>
            {
                var paymentEntity = p.GetBlob(_Networks);
                if (paymentEntity is null)
                    return null;
                // PaymentEntity on version 0 does not have their own fee, because it was assumed that the payment method have fixed fee.
                // We want to hide this legacy detail in InvoiceRepository, so we fetch the fee from the PaymentMethod and assign it to the PaymentEntity.
                if (paymentEntity.Version == 0)
                {
                    if (paymentMethods == null)
                        paymentMethods = entity.GetPaymentMethods();
                    var paymentMethodDetails = paymentMethods.TryGet(paymentEntity.GetPaymentMethodId())?.GetPaymentMethodDetails();
                    if (paymentMethodDetails != null) // == null should never happen, but we never know.
                        paymentEntity.NetworkFee = paymentMethodDetails.GetNextNetworkFee();
                }

                return paymentEntity;
            })
            .Where(p => p != null)
            .OrderBy(a => a.ReceivedTime).ToList();
#pragma warning restore CS0618
            var state = invoice.GetInvoiceState();
            entity.ExceptionStatus = state.ExceptionStatus;
            entity.Status = state.Status;
            entity.RefundMail = invoice.CustomerEmail;
            entity.Refundable = false;
            if (invoice.HistoricalAddressInvoices != null)
            {
                entity.HistoricalAddresses = invoice.HistoricalAddressInvoices.ToArray();
            }
            if (invoice.AddressInvoices != null)
            {
                entity.AvailableAddressHashes = invoice.AddressInvoices.Select(a => a.GetAddress() + a.GetpaymentMethodId().ToString()).ToHashSet();
            }
            if (invoice.Events != null)
            {
                entity.Events = invoice.Events.OrderBy(c => c.Timestamp).ToList();
            }
            if (!string.IsNullOrEmpty(entity.RefundMail) && string.IsNullOrEmpty(entity.Metadata.BuyerEmail))
            {
                entity.Metadata.BuyerEmail = entity.RefundMail;
            }
            entity.Archived = invoice.Archived;
            return entity;
        }

        private IQueryable<Data.InvoiceData> GetInvoiceQuery(ApplicationDbContext context, InvoiceQuery queryObject)
        {
            IQueryable<Data.InvoiceData> query = queryObject.UserId is null
                ? context.Invoices
                : context.UserStore
                    .Where(u => u.ApplicationUserId == queryObject.UserId)
                    .SelectMany(c => c.StoreData.Invoices);

            if (!queryObject.IncludeArchived)
            {
                query = query.Where(i => !i.Archived);
            }

            if (queryObject.InvoiceId != null && queryObject.InvoiceId.Length > 0)
            {
                var statusSet = queryObject.InvoiceId.ToHashSet().ToArray();
                query = query.Where(i => statusSet.Contains(i.Id));
            }

            if (queryObject.StoreId != null && queryObject.StoreId.Length > 0)
            {
                var stores = queryObject.StoreId.ToHashSet().ToArray();
                query = query.Where(i => stores.Contains(i.StoreDataId));
            }

            if (!string.IsNullOrEmpty(queryObject.TextSearch))
            {
#pragma warning disable CA1307 // Specify StringComparison
                query = query.Where(i => i.InvoiceSearchData.Any(data => data.Value.StartsWith(queryObject.TextSearch)));
#pragma warning restore CA1307 // Specify StringComparison
            }

            if (queryObject.StartDate != null)
                query = query.Where(i => queryObject.StartDate.Value <= i.Created);

            if (queryObject.EndDate != null)
                query = query.Where(i => i.Created <= queryObject.EndDate.Value);

            if (queryObject.OrderId != null && queryObject.OrderId.Length > 0)
            {
                var statusSet = queryObject.OrderId.ToHashSet().ToArray();
                query = query.Where(i => statusSet.Contains(i.OrderId));
            }
            if (queryObject.ItemCode != null && queryObject.ItemCode.Length > 0)
            {
                var statusSet = queryObject.ItemCode.ToHashSet().ToArray();
                query = query.Where(i => statusSet.Contains(i.ItemCode));
            }

            if (queryObject.Status != null && queryObject.Status.Length > 0)
            {
                var statusSet = queryObject.Status.ToHashSet().ToArray();
                query = query.Where(i => statusSet.Contains(i.Status));
            }

            if (queryObject.Unusual != null)
            {
                var unused = queryObject.Unusual.Value;
                query = query.Where(i => unused == (i.Status == "invalid" || !string.IsNullOrEmpty(i.ExceptionStatus)));
            }

            if (queryObject.ExceptionStatus != null && queryObject.ExceptionStatus.Length > 0)
            {
                var exceptionStatusSet = queryObject.ExceptionStatus.Select(s => NormalizeExceptionStatus(s)).ToHashSet().ToArray();
                query = query.Where(i => exceptionStatusSet.Contains(i.ExceptionStatus));
            }

            query = query.OrderByDescending(q => q.Created);

            if (queryObject.Skip != null)
                query = query.Skip(queryObject.Skip.Value);

            if (queryObject.Take != null)
                query = query.Take(queryObject.Take.Value);
            return query;
        }

        public async Task<int> GetInvoicesTotal(InvoiceQuery queryObject)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var query = GetInvoiceQuery(context, queryObject);
                return await query.CountAsync();
            }
        }

        public async Task<InvoiceEntity[]> GetInvoices(InvoiceQuery queryObject)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var query = GetInvoiceQuery(context, queryObject);
                query = query.Include(o => o.Payments);
                if (queryObject.IncludeAddresses)
                    query = query.Include(o => o.HistoricalAddressInvoices).Include(o => o.AddressInvoices);
                if (queryObject.IncludeEvents)
                    query = query.Include(o => o.Events);
                var data = await query.ToArrayAsync().ConfigureAwait(false);
                return data.Select(ToEntity).ToArray();
            }
        }

        private string NormalizeExceptionStatus(string status)
        {
            status = status.ToLowerInvariant();
            switch (status)
            {
                case "paidover":
                case "over":
                case "overpaid":
                    status = "paidOver";
                    break;
                case "paidlate":
                case "late":
                    status = "paidLate";
                    break;
                case "paidpartial":
                case "underpaid":
                case "partial":
                    status = "paidPartial";
                    break;
            }
            return status;
        }

        /// <summary>
        /// Add a payment to an invoice
        /// </summary>
        /// <param name="invoiceId"></param>
        /// <param name="date"></param>
        /// <param name="paymentData"></param>
        /// <param name="cryptoCode"></param>
        /// <param name="accounted"></param>
        /// <returns>The PaymentEntity or null if already added</returns>
        public async Task<PaymentEntity> AddPayment(string invoiceId, DateTimeOffset date, CryptoPaymentData paymentData, BTCPayNetworkBase network, bool accounted = false)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var invoice = context.Invoices.Find(invoiceId);
                if (invoice == null)
                    return null;
                InvoiceEntity invoiceEntity = invoice.GetBlob(_Networks);
                PaymentMethod paymentMethod = invoiceEntity.GetPaymentMethod(new PaymentMethodId(network.CryptoCode, paymentData.GetPaymentType()));
                IPaymentMethodDetails paymentMethodDetails = paymentMethod.GetPaymentMethodDetails();
                PaymentEntity entity = new PaymentEntity
                {
                    Version = 1,
#pragma warning disable CS0618
                    CryptoCode = network.CryptoCode,
#pragma warning restore CS0618
                    ReceivedTime = date.UtcDateTime,
                    Accounted = accounted,
                    NetworkFee = paymentMethodDetails.GetNextNetworkFee(),
                    Network = network
                };
                entity.SetCryptoPaymentData(paymentData);
                //TODO: abstract
                if (paymentMethodDetails is Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod bitcoinPaymentMethod &&
                    bitcoinPaymentMethod.NetworkFeeMode == NetworkFeeMode.MultiplePaymentsOnly &&
                    bitcoinPaymentMethod.NextNetworkFee == Money.Zero)
                {
                    bitcoinPaymentMethod.NextNetworkFee = bitcoinPaymentMethod.NetworkFeeRate.GetFee(100); // assume price for 100 bytes
                    paymentMethod.SetPaymentMethodDetails(bitcoinPaymentMethod);
                    invoiceEntity.SetPaymentMethod(paymentMethod);
                    invoice.Blob = ToBytes(invoiceEntity, network);
                }
                PaymentData data = new PaymentData
                {
                    Id = paymentData.GetPaymentId(),
                    Blob = ToBytes(entity, entity.Network),
                    InvoiceDataId = invoiceId,
                    Accounted = accounted
                };

                await context.Payments.AddAsync(data);

                AddToTextSearch(context, invoice, paymentData.GetSearchTerms());
                try
                {
                    await context.SaveChangesAsync().ConfigureAwait(false);
                }
                catch (DbUpdateException) { return null; } // Already exists
                return entity;
            }
        }

        public async Task UpdatePayments(List<PaymentEntity> payments)
        {
            if (payments.Count == 0)
                return;
            using (var context = _ContextFactory.CreateContext())
            {
                foreach (var payment in payments)
                {
                    var paymentData = payment.GetCryptoPaymentData();
                    var data = new PaymentData();
                    data.Id = paymentData.GetPaymentId();
                    data.Accounted = payment.Accounted;
                    data.Blob = ToBytes(payment, payment.Network);
                    context.Attach(data);
                    context.Entry(data).Property(o => o.Accounted).IsModified = true;
                    context.Entry(data).Property(o => o.Blob).IsModified = true;
                }
                await context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        private static byte[] ToBytes<T>(T obj, BTCPayNetworkBase network = null)
        {
            return ZipUtils.Zip(ToJsonString(obj, network));
        }

        public static string ToJsonString<T>(T data, BTCPayNetworkBase network)
        {
            if (network == null)
            {
                return JsonConvert.SerializeObject(data, DefaultSerializerSettings);
            }
            return network.ToString(data);
        }
    }

    public class InvoiceQuery
    {
        public string[] StoreId
        {
            get; set;
        }
        public string UserId
        {
            get; set;
        }
        public string TextSearch
        {
            get; set;
        }
        public DateTimeOffset? StartDate
        {
            get; set;
        }

        public DateTimeOffset? EndDate
        {
            get; set;
        }

        public int? Skip
        {
            get; set;
        }

        public int? Take
        {
            get; set;
        }

        public string[] OrderId
        {
            get; set;
        }

        public string[] ItemCode
        {
            get; set;
        }

        public bool? Unusual { get; set; }

        public string[] Status
        {
            get; set;
        }

        public string[] ExceptionStatus
        {
            get; set;
        }

        public string[] InvoiceId
        {
            get;
            set;
        }
        public bool IncludeAddresses { get; set; }

        public bool IncludeEvents { get; set; }
        public bool IncludeArchived { get; set; } = true;
    }
}
