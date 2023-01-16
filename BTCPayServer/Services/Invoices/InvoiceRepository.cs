using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
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

        public Logs Logs { get; }

        private readonly ApplicationDbContextFactory _applicationDbContextFactory;
        private readonly EventAggregator _eventAggregator;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;

        public InvoiceRepository(ApplicationDbContextFactory contextFactory,
            BTCPayNetworkProvider networks, EventAggregator eventAggregator, Logs logs)
        {
            Logs = logs;
            _applicationDbContextFactory = contextFactory;
            _btcPayNetworkProvider = networks;
            _eventAggregator = eventAggregator;
        }

        public async Task<Data.WebhookDeliveryData> GetWebhookDelivery(string invoiceId, string deliveryId)
        {
            using var ctx = _applicationDbContextFactory.CreateContext();
            return await ctx.InvoiceWebhookDeliveries
                .Where(d => d.InvoiceId == invoiceId && d.DeliveryId == deliveryId)
                .Select(d => d.Delivery)
                .FirstOrDefaultAsync();
        }

        public InvoiceEntity CreateNewInvoice()
        {
            return new InvoiceEntity()
            {
                Networks = _btcPayNetworkProvider,
                Version = InvoiceEntity.Lastest_Version,
                InvoiceTime = DateTimeOffset.UtcNow,
                Metadata = new InvoiceMetadata()
            };
        }

        public async Task<bool> RemovePendingInvoice(string invoiceId)
        {
            using var ctx = _applicationDbContextFactory.CreateContext();
            ctx.PendingInvoices.Remove(new PendingInvoiceData() { Id = invoiceId });
            try
            {
                await ctx.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException) { return false; }
        }

        public async Task<IEnumerable<InvoiceEntity>> GetInvoicesFromAddresses(string[] addresses)
        {
            using var db = _applicationDbContextFactory.CreateContext();
            return (await db.AddressInvoices
                .Include(a => a.InvoiceData.Payments)
#pragma warning disable CS0618
                    .Where(a => addresses.Contains(a.Address))
#pragma warning restore CS0618
                    .Select(a => a.InvoiceData)
                .ToListAsync()).Select(ToEntity);
        }

        public async Task<InvoiceEntity[]> GetPendingInvoices(bool includeAddressData = false, bool skipNoPaymentInvoices = false)
        {
            using var ctx = _applicationDbContextFactory.CreateContext();
            var q = ctx.PendingInvoices.AsQueryable();
            q = q.Include(o => o.InvoiceData)
                 .ThenInclude(o => o.Payments);
            if (includeAddressData)
                q = q.Include(o => o.InvoiceData)
                    .ThenInclude(o => o.AddressInvoices);
            if (skipNoPaymentInvoices)
                q = q.Where(i => i.InvoiceData.Payments.Any());
            return (await q.Select(o => o.InvoiceData).ToArrayAsync()).Select(ToEntity).ToArray();
        }
        public async Task<string[]> GetPendingInvoiceIds()
        {
            using var ctx = _applicationDbContextFactory.CreateContext();
            return await ctx.PendingInvoices.AsQueryable().Select(data => data.Id).ToArrayAsync();
        }

        public async Task<List<Data.WebhookDeliveryData>> GetWebhookDeliveries(string invoiceId)
        {
            using var ctx = _applicationDbContextFactory.CreateContext();
            return await ctx.InvoiceWebhookDeliveries
                .Include(s => s.Delivery).ThenInclude(s => s.Webhook)
                .Where(s => s.InvoiceId == invoiceId)
                .Select(s => s.Delivery)
                .OrderByDescending(s => s.Timestamp)
                .ToListAsync();
        }

        public async Task<AppData[]> GetAppsTaggingStore(string storeId)
        {
            ArgumentNullException.ThrowIfNull(storeId);
            using var ctx = _applicationDbContextFactory.CreateContext();
            return await ctx.Apps.Where(a => a.StoreDataId == storeId && a.TagAllInvoices).ToArrayAsync();
        }

        public async Task UpdateInvoice(string invoiceId, UpdateCustomerModel data)
        {
            using var ctx = _applicationDbContextFactory.CreateContext();
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

        public async Task UpdateInvoiceExpiry(string invoiceId, TimeSpan seconds)
        {
            await using var ctx = _applicationDbContextFactory.CreateContext();
            var invoiceData = await ctx.Invoices.FindAsync(invoiceId);
            var invoice = invoiceData.GetBlob(_btcPayNetworkProvider);
            var expiry = DateTimeOffset.Now + seconds;
            invoice.ExpirationTime = expiry;
            invoice.MonitoringExpiration = expiry.AddHours(1);
            invoiceData.Blob = ToBytes(invoice, _btcPayNetworkProvider.DefaultNetwork);

            await ctx.SaveChangesAsync();

            _eventAggregator.Publish(new InvoiceDataChangedEvent(invoice));
            _ = InvoiceNeedUpdateEventLater(invoiceId, seconds);
        }

        async Task InvoiceNeedUpdateEventLater(string invoiceId, TimeSpan expirationIn)
        {
            await Task.Delay(expirationIn);
            _eventAggregator.Publish(new InvoiceNeedUpdateEvent(invoiceId));
        }

        public async Task ExtendInvoiceMonitor(string invoiceId)
        {
            using var ctx = _applicationDbContextFactory.CreateContext();
            var invoiceData = await ctx.Invoices.FindAsync(invoiceId);

            var invoice = invoiceData.GetBlob(_btcPayNetworkProvider);
            invoice.MonitoringExpiration = invoice.MonitoringExpiration.AddHours(1);
            invoiceData.Blob = ToBytes(invoice, null);

            await ctx.SaveChangesAsync();
        }

        public async Task<InvoiceEntity> CreateInvoiceAsync(string storeId, InvoiceEntity invoice, string[] additionalSearchTerms = null)
        {
            var textSearch = new HashSet<string>();
            invoice = Clone(invoice);
            invoice.Networks = _btcPayNetworkProvider;
            invoice.Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16));
#pragma warning disable CS0618
            invoice.Payments = new List<PaymentEntity>();
#pragma warning restore CS0618
            invoice.StoreId = storeId;
            using (var context = _applicationDbContextFactory.CreateContext())
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
                    textSearch.Add(paymentDestination);
                    textSearch.Add(paymentMethod.Calculate().TotalDue.ToString());
                }
                await context.PendingInvoices.AddAsync(new PendingInvoiceData() { Id = invoice.Id });

                textSearch.Add(invoice.Id);
                textSearch.Add(invoice.InvoiceTime.ToString(CultureInfo.InvariantCulture));
                if (!invoice.IsUnsetTopUp())
                    textSearch.Add(invoice.Price.ToString(CultureInfo.InvariantCulture));
                textSearch.Add(invoice.Metadata.OrderId);
                textSearch.Add(invoice.StoreId);
                textSearch.Add(invoice.Metadata.BuyerEmail);

                if (additionalSearchTerms != null)
                {
                    textSearch.AddRange(additionalSearchTerms);
                }
                AddToTextSearch(context, invoiceData, textSearch.ToArray());

                await context.SaveChangesAsync().ConfigureAwait(false);
            }


            return invoice;
        }

        private InvoiceEntity Clone(InvoiceEntity invoice)
        {
            var temp = new InvoiceData();
            temp.Blob = ToBytes(invoice);
            return temp.GetBlob(_btcPayNetworkProvider);
        }

        public async Task AddInvoiceLogs(string invoiceId, InvoiceLogs logs)
        {
            await using var context = _applicationDbContextFactory.CreateContext();
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
            await using var context = _applicationDbContextFactory.CreateContext();
            var invoice = (await context.Invoices.Where(i => i.Id == invoiceId).ToListAsync()).FirstOrDefault();
            if (invoice == null)
                return false;

            var invoiceEntity = invoice.GetBlob(_btcPayNetworkProvider);
            var paymentMethod = invoiceEntity.GetPaymentMethod(network, paymentMethodDetails.GetPaymentType());
            if (paymentMethod == null)
                return false;

            var existingPaymentMethod = paymentMethod.GetPaymentMethodDetails();
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

            AddToTextSearch(context, invoice, paymentMethodDetails.GetPaymentDestination());
            await context.SaveChangesAsync();
            return true;
        }

        public async Task UpdateInvoicePaymentMethod(string invoiceId, PaymentMethod paymentMethod)
        {
            using var context = _applicationDbContextFactory.CreateContext();
            var invoice = await context.Invoices.FindAsync(invoiceId);
            if (invoice == null)
                return;
            var network = paymentMethod.Network;
            var invoiceEntity = invoice.GetBlob(_btcPayNetworkProvider);
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
            }
            invoiceEntity.SetPaymentMethod(paymentMethod);
            invoice.Blob = ToBytes(invoiceEntity, network);
            AddToTextSearch(context, invoice, paymentMethod.GetPaymentMethodDetails().GetPaymentDestination());
            await context.SaveChangesAsync();
        }

        public async Task AddPendingInvoiceIfNotPresent(string invoiceId)
        {
            using var context = _applicationDbContextFactory.CreateContext();
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

        public async Task AddInvoiceEvent(string invoiceId, object evt, InvoiceEventData.EventSeverity severity)
        {
            await using var context = _applicationDbContextFactory.CreateContext();
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

        public static void AddToTextSearch(ApplicationDbContext context, InvoiceData invoice, params string[] terms)
        {
            var filteredTerms = terms.Where(t => !string.IsNullOrWhiteSpace(t)
                && (invoice.InvoiceSearchData == null || invoice.InvoiceSearchData.All(data => data.Value != t)))
                .Distinct()
                .Select(s => new InvoiceSearchData() { InvoiceDataId = invoice.Id, Value = s.Truncate(512) });
            context.AddRange(filteredTerms);
        }

        public static void RemoveFromTextSearch(ApplicationDbContext context, InvoiceData invoice,
            string term)
        {
            var query = context.InvoiceSearches.AsQueryable();
            var filteredQuery = query.Where(st => st.InvoiceDataId.Equals(invoice.Id) && st.Value.Equals(term));
            context.InvoiceSearches.RemoveRange(filteredQuery);
        }

        public async Task UpdateInvoiceStatus(string invoiceId, InvoiceState invoiceState)
        {
            using var context = _applicationDbContextFactory.CreateContext();
            var invoiceData = await context.FindAsync<Data.InvoiceData>(invoiceId).ConfigureAwait(false);
            if (invoiceData == null)
                return;
            invoiceData.Status = InvoiceState.ToString(invoiceState.Status);
            invoiceData.ExceptionStatus = InvoiceState.ToString(invoiceState.ExceptionStatus);
            await context.SaveChangesAsync().ConfigureAwait(false);
        }
        internal async Task UpdateInvoicePrice(string invoiceId, InvoiceEntity invoice)
        {
            if (invoice.Type != InvoiceType.TopUp)
                throw new ArgumentException("The invoice type should be TopUp to be able to update invoice price", nameof(invoice));
            using var context = _applicationDbContextFactory.CreateContext();
            var invoiceData = await context.FindAsync<Data.InvoiceData>(invoiceId).ConfigureAwait(false);
            if (invoiceData == null)
                return;
            var blob = invoiceData.GetBlob(_btcPayNetworkProvider);
            blob.Price = invoice.Price;
            AddToTextSearch(context, invoiceData, new[] { invoice.Price.ToString(CultureInfo.InvariantCulture) });
            invoiceData.Blob = ToBytes(blob, null);
            await context.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task MassArchive(string[] invoiceIds, bool archive = true)
        {
            using var context = _applicationDbContextFactory.CreateContext();
            var items = context.Invoices.Where(a => invoiceIds.Contains(a.Id));
            if (items == null)
            {
                return;
            }

            foreach (InvoiceData invoice in items)
            {
                invoice.Archived = archive;
            }

            await context.SaveChangesAsync();
        }

        public async Task ToggleInvoiceArchival(string invoiceId, bool archived, string storeId = null)
        {
            using var context = _applicationDbContextFactory.CreateContext();
            var invoiceData = await context.FindAsync<InvoiceData>(invoiceId).ConfigureAwait(false);
            if (invoiceData == null || invoiceData.Archived == archived ||
                (storeId != null &&
                 !invoiceData.StoreDataId.Equals(storeId, StringComparison.InvariantCultureIgnoreCase)))
                return;
            invoiceData.Archived = archived;
            await context.SaveChangesAsync().ConfigureAwait(false);
        }
        public async Task<InvoiceEntity> UpdateInvoiceMetadata(string invoiceId, string storeId, JObject metadata)
        {
            using var context = _applicationDbContextFactory.CreateContext();
            var invoiceData = await GetInvoiceRaw(invoiceId, context);
            if (invoiceData == null || (storeId != null &&
                                        !invoiceData.StoreDataId.Equals(storeId,
                                            StringComparison.InvariantCultureIgnoreCase)))
                return null;
            var blob = invoiceData.GetBlob(_btcPayNetworkProvider);

            var newMetadata = InvoiceMetadata.FromJObject(metadata);
            var oldOrderId = blob.Metadata.OrderId;
            var newOrderId = newMetadata.OrderId;

            if (newOrderId != oldOrderId)
            {
                // OrderId is saved in 2 places: (1) the invoice table and (2) in the metadata field. We are updating both for consistency.
                invoiceData.OrderId = newOrderId;

                if (oldOrderId != null && (newOrderId is null || !newOrderId.Equals(oldOrderId, StringComparison.InvariantCulture)))
                {
                    RemoveFromTextSearch(context, invoiceData, oldOrderId);
                }
                if (newOrderId != null)
                {
                    AddToTextSearch(context, invoiceData, new[] { newOrderId });
                }
            }

            blob.Metadata = newMetadata;
            invoiceData.Blob = ToBytes(blob);
            await context.SaveChangesAsync().ConfigureAwait(false);
            return ToEntity(invoiceData);
        }
        public async Task<bool> MarkInvoiceStatus(string invoiceId, InvoiceStatus status)
        {
            using (var context = _applicationDbContextFactory.CreateContext())
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

        public async Task<InvoiceEntity> GetInvoice(string id, bool includeAddressData = false)
        {
            using var context = _applicationDbContextFactory.CreateContext();
            var res = await GetInvoiceRaw(id, context, includeAddressData);
            return res == null ? null : ToEntity(res);
        }
        public async Task<InvoiceEntity[]> GetInvoices(string[] invoiceIds)
        {
            var invoiceIdSet = invoiceIds.ToHashSet();
            using var context = _applicationDbContextFactory.CreateContext();
            IQueryable<Data.InvoiceData> query =
                context
                .Invoices
                .Include(o => o.Payments)
                .Where(o => invoiceIdSet.Contains(o.Id));

            return (await query.ToListAsync()).Select(o => ToEntity(o)).ToArray();
        }

        private async Task<InvoiceData> GetInvoiceRaw(string id, ApplicationDbContext dbContext, bool includeAddressData = false)
        {
            IQueryable<Data.InvoiceData> query =
                    dbContext
                    .Invoices
                    .Include(o => o.Payments);
            if (includeAddressData)
                query = query.Include(o => o.AddressInvoices);
            query = query.Where(i => i.Id == id);

            var invoice = (await query.ToListAsync()).FirstOrDefault();
            if (invoice == null)
                return null;

            return invoice;
        }

        public InvoiceEntity ToEntity(Data.InvoiceData invoice)
        {
            var entity = invoice.GetBlob(_btcPayNetworkProvider);
            PaymentMethodDictionary paymentMethods = null;
#pragma warning disable CS0618
            entity.Payments = invoice.Payments.Select(p =>
            {
                var paymentEntity = p.GetBlob(_btcPayNetworkProvider);
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
            if (invoice.AddressInvoices != null)
            {
                entity.AvailableAddressHashes = invoice.AddressInvoices.Select(a => a.GetAddress() + a.GetPaymentMethodId().ToString()).ToHashSet();
            }
            if (invoice.Events != null)
            {
                entity.Events = invoice.Events.OrderBy(c => c.Timestamp).ToList();
            }
            if (invoice.Refunds != null)
            {
                entity.Refunds = invoice.Refunds.OrderBy(c => c.PullPaymentData.StartDate).ToList();
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
                if (queryObject.InvoiceId.Length > 1)
                {
                    var statusSet = queryObject.InvoiceId.ToHashSet().ToArray();
                    query = query.Where(i => statusSet.Contains(i.Id));
                }
                else
                {
                    var invoiceId = queryObject.InvoiceId.First();
                    query = query.Where(i => i.Id == invoiceId);
                }
            }

            if (queryObject.StoreId != null && queryObject.StoreId.Length > 0)
            {
                if (queryObject.StoreId.Length > 1)
                {
                    var stores = queryObject.StoreId.ToHashSet().ToArray();
                    query = query.Where(i => stores.Contains(i.StoreDataId));
                }
                // Big performant improvement to use Where rather than Contains when possible
                // In our test, the first gives  720.173 ms vs 40.735 ms
                else
                {
                    var storeId = queryObject.StoreId.First();
                    query = query.Where(i => i.StoreDataId == storeId);
                }
            }

            if (!string.IsNullOrEmpty(queryObject.TextSearch))
            {
                var text = queryObject.TextSearch.Truncate(512);
#pragma warning disable CA1310 // Specify StringComparison
                query = query.Where(i => i.InvoiceSearchData.Any(data => data.Value.StartsWith(text)));
#pragma warning restore CA1310 // Specify StringComparison
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
                var statusSet = queryObject.Status.ToHashSet();
                // We make sure here that the old filters still work
                foreach (var status in queryObject.Status.Select(s => s.ToLowerInvariant()))
                {
                    if (status == "paid")
                        statusSet.Add("processing");
                    if (status == "processing")
                        statusSet.Add("paid");
                    if (status == "confirmed")
                    {
                        statusSet.Add("complete");
                        statusSet.Add("settled");
                    }
                    if (status == "settled")
                    {
                        statusSet.Add("complete");
                        statusSet.Add("confirmed");
                    }
                    if (status == "complete")
                    {
                        statusSet.Add("settled");
                        statusSet.Add("confirmed");
                    }
                }
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

        public async Task<InvoiceEntity[]> GetInvoices(InvoiceQuery queryObject)
        {
            using var context = _applicationDbContextFactory.CreateContext();
            var query = GetInvoiceQuery(context, queryObject);
            query = query.Include(o => o.Payments);
            if (queryObject.IncludeAddresses)
                query = query.Include(o => o.AddressInvoices);
            if (queryObject.IncludeEvents)
                query = query.Include(o => o.Events);
            if (queryObject.IncludeRefunds)
                query = query.Include(o => o.Refunds).ThenInclude(refundData => refundData.PullPaymentData);
            var data = await query.ToArrayAsync().ConfigureAwait(false);
            return data.Select(ToEntity).ToArray();
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

        public static byte[] ToBytes<T>(T obj, BTCPayNetworkBase network = null)
        {
            return ZipUtils.Zip(ToJsonString(obj, network));
        }

        public static T FromBytes<T>(byte[] blob, BTCPayNetworkBase network = null)
        {
            return network == null
                ? JsonConvert.DeserializeObject<T>(ZipUtils.Unzip(blob), DefaultSerializerSettings)
                : network.ToObject<T>(ZipUtils.Unzip(blob));
        }

        public static string ToJsonString<T>(T data, BTCPayNetworkBase network)
        {
            return network == null ? JsonConvert.SerializeObject(data, DefaultSerializerSettings) : network.ToString(data);
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
        public bool IncludeRefunds { get; set; }
    }
}
