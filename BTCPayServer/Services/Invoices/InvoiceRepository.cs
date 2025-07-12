using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
using Dapper;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using Encoders = NBitcoin.DataEncoders.Encoders;
using InvoiceData = BTCPayServer.Data.InvoiceData;

namespace BTCPayServer.Services.Invoices
{
    public class InvoiceRepository
    {
        internal static JsonSerializerSettings DefaultSerializerSettings;
        static InvoiceRepository()
        {
            DefaultSerializerSettings = new JsonSerializerSettings();
            NBitcoin.JsonConverters.Serializer.RegisterFrontConverters(DefaultSerializerSettings);
        }

        private readonly ApplicationDbContextFactory _applicationDbContextFactory;
        private readonly EventAggregator _eventAggregator;
        public ApplicationDbContextFactory DbContextFactory => _applicationDbContextFactory;
        public InvoiceRepository(ApplicationDbContextFactory contextFactory,
            EventAggregator eventAggregator)
        {
            _applicationDbContextFactory = contextFactory;
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

        public InvoiceEntity CreateNewInvoice(string storeId)
        {
            return new InvoiceEntity()
            {
                Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16)),
                StoreId = storeId,
                Version = InvoiceEntity.Lastest_Version,
                // Truncating was an unintended side effect of previous code. Might want to remove that one day
                InvoiceTime = DateTimeOffset.UtcNow.TruncateMilliSeconds(),
                Metadata = new InvoiceMetadata(),
#pragma warning disable CS0618
                Payments = new List<PaymentEntity>()
#pragma warning restore CS0618
            };
        }

        public async Task<InvoiceEntity> GetInvoiceFromAddress(PaymentMethodId paymentMethodId, string address)
        {
            using var db = _applicationDbContextFactory.CreateContext();
            var row = (await db.AddressInvoices
            .Include(a => a.InvoiceData.Payments)
                .Where(a => a.Address == address && a.PaymentMethodId == paymentMethodId.ToString())
                .Select(a => a.InvoiceData)
            .FirstOrDefaultAsync());
            return row is null ? null : ToEntity(row);
        }

        /// <summary>
        /// Returns all invoices which either:
        /// * Have the <paramref name="paymentMethodId"/> activated and are pending
        /// * Aren't pending but have a payment from the <paramref name="paymentMethodId"/> that is pending
        /// <see cref="InvoiceData.AddressInvoices"/> is filled with the monitored addresses of the <paramref name="paymentMethodId"/> for this invoice.
        /// <see cref="InvoiceData.Payments"/> include the <paramref name="paymentMethodId"/> payments for this invoice.
        /// </summary>
        /// <param name="paymentMethodId">The payment method id</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        public Task<InvoiceEntity[]> GetMonitoredInvoices(PaymentMethodId paymentMethodId, CancellationToken cancellationToken = default)
        => GetMonitoredInvoices(paymentMethodId, false, cancellationToken: cancellationToken);

        /// <summary>
        /// Returns all invoices which either:
        /// * Have the <paramref name="paymentMethodId"/> activated and are pending
        /// * Aren't pending but have a payment from the <paramref name="paymentMethodId"/> that is pending
        /// <see cref="InvoiceData.AddressInvoices"/> is filled with the monitored addresses of the <paramref name="paymentMethodId"/> for this invoice.
        /// <see cref="InvoiceData.Payments"/> include the <paramref name="paymentMethodId"/> payments for this invoice.
        /// </summary>
        /// <param name="paymentMethodId">The payment method id</param>
        /// <param name="includeNonActivated">If true, include pending invoice with non activated payment methods</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        public async Task<InvoiceEntity[]> GetMonitoredInvoices(PaymentMethodId paymentMethodId, bool includeNonActivated, CancellationToken cancellationToken = default)
        {
            using var ctx = _applicationDbContextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();

            var rows = await conn.QueryAsync<(string Id, uint xmin, string[] addresses, string[] payments, string invoice)>(new("""
                WITH invoices_payments AS (
                SELECT
                    m.invoice_id,
                    array_agg(to_jsonb(p)) FILTER (WHERE p."Id" IS NOT NULL) as payments
                FROM get_monitored_invoices(@pmi, @includeNonActivated) m
                LEFT JOIN "Payments" p ON p."Id" = m.payment_id AND p."PaymentMethodId" = m.payment_method_id
                GROUP BY 1
                ),
                invoices_addresses AS (
                SELECT m.invoice_id,
                       array_agg(ai."Address") addresses
                FROM get_monitored_invoices(@pmi, @includeNonActivated) m
                JOIN "AddressInvoices" ai ON ai."InvoiceDataId" = m.invoice_id
                WHERE ai."PaymentMethodId" = @pmi
                GROUP BY 1
                )
                SELECT
                    ip.invoice_id, i.xmin, COALESCE(ia.addresses, '{}'), COALESCE(ip.payments, '{}'), to_jsonb(i)
                FROM invoices_payments ip
                JOIN "Invoices" i ON i."Id" = ip.invoice_id
                LEFT JOIN invoices_addresses ia ON ia.invoice_id = ip.invoice_id;
                """
                , new { pmi = paymentMethodId.ToString(), includeNonActivated }));
            if (Enumerable.TryGetNonEnumeratedCount(rows, out var c) && c == 0)
                return Array.Empty<InvoiceEntity>();
            List<InvoiceEntity> invoices = new List<InvoiceEntity>();
            foreach (var row in rows)
            {
                var jobj = JObject.Parse(row.invoice);
                jobj.Remove("Blob");
                jobj["Blob2"] = jobj["Blob2"].ToString(Formatting.None);
                var invoiceData = jobj.ToObject<InvoiceData>();
                invoiceData.XMin = row.xmin;
                invoiceData.AddressInvoices = row.addresses.Select((a) => new AddressInvoiceData() { InvoiceDataId = invoiceData.Id, Address = a, PaymentMethodId = paymentMethodId.ToString() }).ToList();
                invoiceData.Payments = new List<PaymentData>();
                foreach (var payment in row.payments)
                {
                    jobj = JObject.Parse(payment);
                    jobj.Remove("Blob");
                    jobj["Blob2"] = jobj["Blob2"].ToString(Formatting.None);
                    var paymentData = jobj.ToObject<PaymentData>();
                    invoiceData.Payments.Add(paymentData);
                }
                var entity = ToEntity(invoiceData);
                // Disable accounting, as we don't have all the payments...
                // only those related to this paymentMethodId
                entity.DisableAccounting = true;
                invoices.Add(entity);
            }
            return invoices.ToArray();
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

        public async Task<Dictionary<string, RateBook>> GetRatesOfInvoices(HashSet<string> invoiceIds)
        {
            if (invoiceIds.Count == 0)
                return new();
            var res = new Dictionary<string, RateBook>();
            using var ctx = _applicationDbContextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();
            var result = await conn.QueryAsync<(string Id, string Rate, string Currency)>(
                """
                SELECT "Id", "Blob2"->'rates' AS "Rate", "Currency" FROM unnest(@invoices) AS searched_invoices("Id")
                JOIN "Invoices" USING ("Id")
                WHERE "Blob2"->'rates' IS NOT NULL;
                """, new { invoices = invoiceIds.ToArray() });
            foreach (var inv in result)
            {
                var rates = RateBook.Parse(inv.Rate, inv.Currency);
                res.Add(inv.Id, rates);
            }
            return res;
        }

        public async Task<AppData[]> GetAppsTaggingStore(string storeId)
        {
            ArgumentNullException.ThrowIfNull(storeId);
            using var ctx = _applicationDbContextFactory.CreateContext();
            return await ctx.Apps.Where(a => a.StoreDataId == storeId && a.TagAllInvoices).ToArrayAsync();
        }

        public async Task UpdateInvoiceExpiry(string invoiceId, TimeSpan seconds)
        {
retry:
            await using (var ctx = _applicationDbContextFactory.CreateContext())
            {
                var invoiceData = await ctx.Invoices.FindAsync(invoiceId);
                var invoice = invoiceData.GetBlob();
                var expiry = DateTimeOffset.Now + seconds;
                invoice.ExpirationTime = expiry;
                invoice.MonitoringExpiration = expiry.AddHours(1);
                invoiceData.SetBlob(invoice);
                try
                {
                    await ctx.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    goto retry;
                }
                _eventAggregator.Publish(new InvoiceDataChangedEvent(invoice));
                _ = InvoiceNeedUpdateEventLater(invoiceId, seconds);
            }
        }

        async Task InvoiceNeedUpdateEventLater(string invoiceId, TimeSpan expirationIn)
        {
            await Task.Delay(expirationIn);
            _eventAggregator.Publish(new InvoiceNeedUpdateEvent(invoiceId));
        }

        public async Task CreateInvoiceAsync(InvoiceCreationContext creationContext)
        {
            var invoice = creationContext.InvoiceEntity;
            var textSearch = new HashSet<string>();
            using (var context = _applicationDbContextFactory.CreateContext())
            {
                var invoiceData = new InvoiceData
                {
                    StoreDataId = invoice.StoreId,
                    Id = invoice.Id,
                    Status = invoice.Status.ToString(),
                    Archived = false
                };
                invoiceData.SetBlob(invoice);
                await context.Invoices.AddAsync(invoiceData);

                foreach (var ctx in creationContext.PaymentMethodContexts.Where(p => p.Value.Status is PaymentMethodContext.ContextStatus.Created or PaymentMethodContext.ContextStatus.WaitingForActivation))
                {
                    foreach (var trackedDestination in ctx.Value.TrackedDestinations)
                    {
                        await context.AddressInvoices.AddAsync(new AddressInvoiceData()
                        {
                            InvoiceDataId = invoice.Id,
                            Address = trackedDestination,
                            PaymentMethodId = ctx.Key.ToString()
                        });
                    }
                }
                foreach (var prompt in invoice.GetPaymentPrompts())
                {
                    if (prompt.Destination != null)
                        textSearch.Add(prompt.Destination);
                    if (prompt.Activated)
                        textSearch.Add(prompt.Calculate().TotalDue.ToString());
                }

                textSearch.Add(invoice.Id);
                textSearch.Add(invoice.InvoiceTime.ToString(CultureInfo.InvariantCulture));
                if (!invoice.IsUnsetTopUp())
                    textSearch.Add(invoice.Price.ToString(CultureInfo.InvariantCulture));
                textSearch.Add(invoice.Metadata.OrderId);
                textSearch.Add(invoice.StoreId);
                textSearch.Add(invoice.Metadata.BuyerEmail);
                textSearch.AddRange(creationContext.GetAllSearchTerms());
                AddToTextSearch(context, invoiceData, textSearch.ToArray());

                await context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task AddInvoiceLogs(string invoiceId, InvoiceLogs logs)
        {
            await using var context = _applicationDbContextFactory.CreateContext();
            var db = context.Database.GetDbConnection();
            var data = logs.ToList().Select(log => new InvoiceEventData()
            {
                Severity = log.Severity,
                InvoiceDataId = invoiceId,
                Message = log.Log,
                Timestamp = log.Timestamp
            }).ToArray();

            await db.ExecuteAsync(InsertInvoiceEvent, data);
        }

        public async Task<InvoiceEventData[]> GetInvoiceLogs(string invoiceId)
        {
            await using var context = _applicationDbContextFactory.CreateContext();
            var db = context.Database.GetDbConnection();
            return (await db.QueryAsync<InvoiceEventData>("SELECT * FROM \"InvoiceEvents\" WHERE \"InvoiceDataId\"=@InvoiceDataId ORDER BY \"Timestamp\"", new { InvoiceDataId = invoiceId })).ToArray();
        }

        public Task UpdatePaymentDetails(string invoiceId, IPaymentMethodHandler handler, object details)
        {
            return UpdatePaymentDetails(invoiceId, handler.PaymentMethodId, details is null ? null : JToken.FromObject(details, handler.Serializer));

        }
        public async Task UpdatePaymentDetails(string invoiceId, PaymentMethodId paymentMethodId, JToken details)
        {
retry:
            using (var context = _applicationDbContextFactory.CreateContext())
            {
                try
                {
                    var invoice = await context.Invoices.FindAsync(invoiceId);
                    if (invoice == null)
                        return;
                    var invoiceEntity = invoice.GetBlob();
                    var prompt = invoiceEntity.GetPaymentPrompt(paymentMethodId);
                    if (prompt == null)
                        return;
                    prompt.Details = details;
                    invoiceEntity.SetPaymentPrompt(paymentMethodId, prompt);
                    invoice.SetBlob(invoiceEntity);
                    await context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    goto retry;
                }
            }
        }
        public async Task UpdatePrompt(string invoiceId, PaymentPrompt prompt)
        {
retry:
            using (var context = _applicationDbContextFactory.CreateContext())
            {
                try
                {
                    var invoice = await context.Invoices.FindAsync(invoiceId);
                    if (invoice == null)
                        return;
                    var invoiceEntity = invoice.GetBlob();
                    var existingPrompt = invoiceEntity.GetPaymentPrompt(prompt.PaymentMethodId);
                    if (existingPrompt == null)
                        return;
                    invoiceEntity.SetPaymentPrompt(prompt.PaymentMethodId, prompt);
                    invoice.SetBlob(invoiceEntity);
                    await context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    goto retry;
                }
            }
        }
        public async Task NewPaymentPrompt(string invoiceId, PaymentMethodContext paymentPromptContext)
        {
            var prompt = paymentPromptContext.Prompt;
retry:
            using (var context = _applicationDbContextFactory.CreateContext())
            {
                var invoice = await context.Invoices.FindAsync(invoiceId);
                if (invoice == null)
                    return;
                var invoiceEntity = invoice.GetBlob();
                var existing = invoiceEntity.GetPaymentPrompt(prompt.PaymentMethodId);
                if (existing.Destination != prompt.Destination && prompt.Activated && prompt.Destination is not null)
                {
                    foreach (var tracked in paymentPromptContext.TrackedDestinations)
                    {
                        await context.AddressInvoices.AddAsync(new AddressInvoiceData()
                        {
                            InvoiceDataId = invoiceId,
                            Address = tracked,
                            PaymentMethodId = paymentPromptContext.PaymentMethodId.ToString()
                        });
                    }
                    AddToTextSearch(context, invoice, prompt.Destination);
                }
                var search = new List<String>();
                search.Add(prompt.Calculate().TotalDue.ToString());
                invoiceEntity.SetPaymentPrompt(prompt.PaymentMethodId, prompt);
                invoice.SetBlob(invoiceEntity);
                AddToTextSearch(context, invoice, search.Concat(paymentPromptContext.AdditionalSearchTerms).Distinct().ToArray());
                try
                {
                    await context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    goto retry;
                }
            }
        }

        const string InsertInvoiceEvent = "INSERT INTO \"InvoiceEvents\" (\"InvoiceDataId\", \"Severity\", \"Message\", \"Timestamp\") VALUES (@InvoiceDataId, @Severity, @Message, @Timestamp)";

        public async Task AddInvoiceEvent(string invoiceId, object evt, InvoiceEventData.EventSeverity severity)
        {
            await using var context = _applicationDbContextFactory.CreateContext();
            var conn = context.Database.GetDbConnection();
            try
            {
                await conn.ExecuteAsync(InsertInvoiceEvent,
                    new InvoiceEventData()
                    {
                        Severity = severity,
                        InvoiceDataId = invoiceId,
                        Message = evt.ToString(),
                        Timestamp = DateTimeOffset.UtcNow
                    });
            }
            catch (Npgsql.NpgsqlException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
            {
                // Invoice does not exists
            }
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
            await context.Database.GetDbConnection()
                .ExecuteAsync("UPDATE \"Invoices\" SET \"Status\"=@status, \"ExceptionStatus\"=@exstatus WHERE \"Id\"=@id",
                new
                {
                    id = invoiceId,
                    status = invoiceState.Status.ToString(),
                    exstatus = invoiceState.ExceptionStatus is InvoiceExceptionStatus.None ? string.Empty : invoiceState.ExceptionStatus.ToString()
                });
        }
        internal async Task UpdateInvoicePrice(string invoiceId, decimal price)
        {
retry:
            using (var context = _applicationDbContextFactory.CreateContext())
            {
                var invoiceData = await context.FindAsync<Data.InvoiceData>(invoiceId).ConfigureAwait(false);
                if (invoiceData == null)
                    return;
                var blob = invoiceData.GetBlob();
                if (blob.Type != InvoiceType.TopUp)
                    throw new ArgumentException("The invoice type should be TopUp to be able to update invoice price", nameof(invoiceId));
                blob.Price = price;
                AddToTextSearch(context, invoiceData, new[] { price.ToString(CultureInfo.InvariantCulture) });
                invoiceData.SetBlob(blob);
                try
                {
                    await context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    goto retry;
                }
            }
        }

        public async Task MassArchive(string[] invoiceIds, bool archive = true)
        {
            using var context = _applicationDbContextFactory.CreateContext();
            var items = context.Invoices.Where(a => invoiceIds.Contains(a.Id));
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
retry:
            using (var context = _applicationDbContextFactory.CreateContext())
            {
                var invoiceData = await GetInvoiceRaw(invoiceId, context);
                if (invoiceData == null || (storeId != null &&
                                            !invoiceData.StoreDataId.Equals(storeId,
                                                StringComparison.InvariantCultureIgnoreCase)))
                    return null;
                var blob = invoiceData.GetBlob();

                var newMetadata = InvoiceMetadata.FromJObject(metadata);
                var oldOrderId = blob.Metadata.OrderId;
                var newOrderId = newMetadata.OrderId;

                if (newOrderId != oldOrderId)
                {
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
                invoiceData.SetBlob(blob);
                try
                {
                    await context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    goto retry;
                }
                return ToEntity(invoiceData);
            }
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
                switch (status)
                {
                    case InvoiceStatus.Settled:
                        if (!invoiceData.GetInvoiceState().CanMarkComplete())
                        {
                            return false;
                        }

                        eventName = InvoiceEvent.MarkedCompleted;
                        break;
                    case InvoiceStatus.Invalid:
                        if (!invoiceData.GetInvoiceState().CanMarkInvalid())
                        {
                            return false;
                        }
                        eventName = InvoiceEvent.MarkedInvalid;
                        break;
                    default:
                        return false;
                }

                invoiceData.Status = status.ToString();
                invoiceData.ExceptionStatus = InvoiceExceptionStatus.Marked.ToString();
                try
                {
                    await context.SaveChangesAsync();
                }
                finally
                {
                    _eventAggregator.Publish(new InvoiceEvent(ToEntity(invoiceData), eventName));
                }
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
            IQueryable<InvoiceData> query =
                context
                .Invoices
                .Include(o => o.Payments)
                .Where(o => invoiceIdSet.Contains(o.Id));

            return (await query.ToListAsync()).Select(o => ToEntity(o)).ToArray();
        }

        private async Task<InvoiceData> GetInvoiceRaw(string id, ApplicationDbContext dbContext, bool includeAddressData = false)
        {
            IQueryable<InvoiceData> query =
                    dbContext
                    .Invoices
                    .Include(o => o.Payments);
            if (includeAddressData)
                query = query.Include(o => o.AddressInvoices);
            query = query.Where(i => i.Id == id);

            var invoice = (await query.ToListAsync()).FirstOrDefault();
            return invoice;
        }

        public InvoiceEntity ToEntity(InvoiceData invoice)
        {
            return invoice.GetBlob();
        }

        private IQueryable<InvoiceData> GetInvoiceQuery(ApplicationDbContext context, InvoiceQuery queryObject)
        {
            IQueryable<InvoiceData> query = queryObject.UserId is null
                ? context.Invoices
                : context.UserStore
                    .Where(u => u.ApplicationUserId == queryObject.UserId)
                    .SelectMany(c => c.StoreData.Invoices);

            if (!queryObject.IncludeArchived)
            {
                query = query.Where(i => !i.Archived);
            }

            if (queryObject.InvoiceId is { Length: > 0 })
            {
                if (queryObject.InvoiceId.Length > 1)
                {
                    var idSet = queryObject.InvoiceId.ToHashSet().ToArray();
                    query = query.Where(i => idSet.Contains(i.Id));
                }
                else
                {
                    var invoiceId = queryObject.InvoiceId.First();
                    query = query.Where(i => i.Id == invoiceId);
                }
            }

            if (queryObject.StoreId is { Length: > 0 })
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

            if (queryObject.OrderId is { Length: > 0 })
            {
                if (queryObject.OrderId is [var orderId])
                {
                    query = query.Where(i => InvoiceData.GetOrderId(i.Blob2) == orderId);
                }
                else
                {
                    var orderIdSet = queryObject.OrderId.ToHashSet().ToArray();
                    query = query.Where(i => orderIdSet.Contains(InvoiceData.GetOrderId(i.Blob2)));
                }
            }
            if (queryObject.ItemCode is { Length: > 0 })
            {
                if (queryObject.ItemCode is [var itemCode])
                {
                    query = query.Where(i => InvoiceData.GetItemCode(i.Blob2) == itemCode);
                }
                else
                {
                    var itemCodeSet = queryObject.ItemCode.ToHashSet().ToArray();
                    query = query.Where(i => itemCodeSet.Contains(InvoiceData.GetItemCode(i.Blob2)));
                }
            }

            var statusSet = queryObject.Status is { Length: > 0 }
                ? queryObject.Status.Select(NormalizeStatus).Where(n => n is not null).ToHashSet()
                : new HashSet<string>();
            var exceptionStatusSet = queryObject.ExceptionStatus is { Length: > 0 }
                ? queryObject.ExceptionStatus.Select(NormalizeExceptionStatus).Where(n => n is not null).ToHashSet()
                : new HashSet<string>();

            if (statusSet.Any() || exceptionStatusSet.Any())
            {
                Expression<Func<InvoiceData, bool>> statusExpression = null;
                Expression<Func<InvoiceData, bool>> exceptionStatusExpression = null;
                if (statusSet.Count is 1)
                {
                    var status = statusSet.First();
                    statusExpression = i => i.Status == status;
                }
                else if (statusSet.Count is > 1)
                {
                    statusExpression = i => statusSet.Contains(i.Status);
                }

                if (exceptionStatusSet.Count is 1)
                {
                    var exceptionStatus = exceptionStatusSet.First();
                    exceptionStatusExpression = i => i.ExceptionStatus == exceptionStatus;
                }
                else if (exceptionStatusSet.Count is > 1)
                {
                    exceptionStatusExpression = i => exceptionStatusSet.Contains(i.ExceptionStatus);
                }
                var predicate = (statusExpression, exceptionStatusExpression) switch
                {
                    ({ } a, { } b) => (Expression)Expression.Or(a.Body, b.Body),
                    ({ } a, null) => a.Body,
                    (null, { } b) => b.Body,
                    _ => throw new NotSupportedException()
                };
                var expression = Expression.Lambda<Func<InvoiceData, bool>>(predicate, Expression.Parameter(typeof(InvoiceData), "i"));
                expression = expression.ReplaceParameterRef();
                query = query.Where(expression);
            }

            if (queryObject.Unusual != null)
            {
                var unusual = queryObject.Unusual.Value;
                query = query.Where(i => unusual == (i.Status == "Invalid" || !string.IsNullOrEmpty(i.ExceptionStatus)));
            }

            if (queryObject.OrderByDesc)
                query = query.OrderByDescending(q => q.Created);
            else
                query = query.OrderBy(q => q.Created);

            if (queryObject.Skip != null)
                query = query.Skip(queryObject.Skip.Value);

            if (queryObject.Take != null)
                query = query.Take(queryObject.Take.Value);

            return query;
        }
        public Task<InvoiceEntity[]> GetInvoices(InvoiceQuery queryObject)
        {
            return GetInvoices(queryObject, default);
        }
        public async Task<InvoiceEntity[]> GetInvoices(InvoiceQuery queryObject, CancellationToken cancellationToken)
        {
            await using var context = _applicationDbContextFactory.CreateContext();
            var query = GetInvoiceQuery(context, queryObject);
            query = query.Include(o => o.Payments);
            if (queryObject.IncludeAddresses)
                query = query.Include(o => o.AddressInvoices);
            if (queryObject.IncludeRefunds)
                query = query.Include(o => o.Refunds).ThenInclude(refundData => refundData.PullPaymentData);
            var data = await query.AsNoTracking().ToArrayAsync(cancellationToken).ConfigureAwait(false);
            return data.Select(ToEntity).ToArray();
        }

        public async Task AddSearchTerms(string invoiceId, List<string> searchTerms)
        {
            if (searchTerms is null or { Count: 0 })
                return;
            await using var context = _applicationDbContextFactory.CreateContext();
            foreach (var t in searchTerms)
            {
                context.InvoiceSearches.Add(new InvoiceSearchData()
                {
                    InvoiceDataId = invoiceId,
                    Value = t
                });
                await context.SaveChangesAsync();
            }
        }

        public async Task<int> GetInvoiceCount(InvoiceQuery queryObject)
        {
            await using var context = _applicationDbContextFactory.CreateContext();
            return await GetInvoiceQuery(context, queryObject).CountAsync();
        }

        private string NormalizeStatus(string status)
        {
            status = status.ToLowerInvariant();
            return status switch
            {
                "new" => "New",
                "paid" or "processing" => "Processing",
                "complete" or "confirmed" or "settled" => "Settled",
                "expired" => "Expired",
                "invalid" => "Invalid",
                _ => null
            };
        }

        private string NormalizeExceptionStatus(string status)
        {
            status = status.ToLowerInvariant();
            return status switch
            {
                "paidover" or "over" or "overpaid" => "PaidOver",
                "paidlate" or "late" => "PaidLate",
                "paidpartial" or "underpaid" or "partial" => "PaidPartial",
                "none" or "" => "",
                _ => null
            };
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

        public InvoiceStatistics GetContributionsByPaymentMethodId(string currency, InvoiceEntity[] invoices, bool softcap)
        {
            decimal totalSettledCurrency = 0.0m;
            decimal totalProcessingCurrency = 0.0m;
            var contributions = invoices
                .Where(p => p.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase))
                .SelectMany(p =>
                {
                    var contribution = new InvoiceStatistics.Contribution
                    {
                        GroupKey = p.Currency,
                        Currency = p.Currency,
                        CurrencyValue = p.Price
                    };
                    contribution.Value = contribution.CurrencyValue;

                    // For hardcap, we count newly created invoices as part of the contributions
                    if (!softcap && p.Status == InvoiceStatus.New)
                    {
                        totalProcessingCurrency += contribution.CurrencyValue;
                        return new[] { contribution };
                    }

                    var markedSettled = p is
                    {
                        Status: InvoiceStatus.Settled,
                        ExceptionStatus: InvoiceExceptionStatus.Marked
                    };
                    // If the user get a donation via other mean, he can register an invoice manually for such amount
                    // then mark the invoice as complete
                    var payments = p.GetPayments(true);
                    if (payments.Count == 0 && markedSettled)
                    {
                        totalSettledCurrency += contribution.CurrencyValue;
                        return new[] { contribution };
                    }

                    // If an invoice has been marked invalid, remove the contribution
                    if (p is
                        {
                            Status: InvoiceStatus.Invalid,
                            ExceptionStatus: InvoiceExceptionStatus.Marked
                        })
                    {
                        contribution.CurrencyValue = 0m;
                        contribution.Value = 0m;
                        return new[] { contribution };
                    }

                    // Else, we just sum the payments
                    return payments
                             .Select(pay =>
                             {
                                 var paymentMethodContribution = new InvoiceStatistics.Contribution
                                 {
                                     GroupKey = GetGroupKey(pay),
                                     Currency = pay.Currency,
                                     CurrencyValue = pay.InvoicePaidAmount.Net,
                                     Value = pay.PaidAmount.Net,
                                     Divisibility = pay.Divisibility
                                 };

                                 // If paid before expiration or marked settled, account the payments...
                                 if (pay.ReceivedTime <= p.ExpirationTime || markedSettled)
                                 {
                                     if (pay.Status is PaymentStatus.Settled)
                                         totalSettledCurrency += pay.InvoicePaidAmount.Net;
                                     else if (pay.Status is PaymentStatus.Processing)
                                         totalProcessingCurrency += pay.InvoicePaidAmount.Net;
                                 }
                                 else
                                 {
                                     paymentMethodContribution.CurrencyValue = 0m;
                                     paymentMethodContribution.Value = 0m;
                                 }
                                 return paymentMethodContribution;
                             })
                             .ToArray();
                })
                .GroupBy(p => p.GroupKey)
                .ToDictionary(p => p.Key, p => new InvoiceStatistics.Contribution
                {
                    Currency = p.Select(p => p.Currency).First(),
                    Divisibility = p.Max(p => p.Divisibility),
                    Value = p.Select(v => v.Value).Sum(),
                    CurrencyValue = p.Select(v => v.CurrencyValue).Sum()
                });
            return new InvoiceStatistics(contributions)
            {
                TotalSettled = totalSettledCurrency,
                TotalProcessing = totalProcessingCurrency,
                Total = totalSettledCurrency + totalProcessingCurrency
            };
        }

        private string GetGroupKey(PaymentEntity pay)
        {
            // If BTC-CHAIN, group by BTC
            if (PaymentTypes.CHAIN.GetPaymentMethodId(pay.Currency) == pay.PaymentMethodId)
                return pay.Currency;
            // If BTC-LN|LNURL, group them together by BTC-LN
            if (PaymentTypes.LN.GetPaymentMethodId(pay.Currency) == pay.PaymentMethodId ||
                PaymentTypes.LNURL.GetPaymentMethodId(pay.Currency) == pay.PaymentMethodId)
                return PaymentTypes.LN.GetPaymentMethodId(pay.Currency).ToString();
            // Else just group by payment method id
            return pay.PaymentMethodId.ToString();
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
        public bool IncludeArchived { get; set; } = true;
        public bool IncludeRefunds { get; set; }
        public bool OrderByDesc { get; set; } = true;
    }

    public class InvoiceStatistics : Dictionary<string, InvoiceStatistics.Contribution>
    {
        public InvoiceStatistics(IEnumerable<KeyValuePair<string, Contribution>> collection) : base(collection)
        {
        }
        public decimal TotalSettled { get; set; }
        public decimal TotalProcessing { get; set; }
        /// <summary>
        /// <see cref="TotalSettled"/> + <see cref="TotalProcessing"/>
        /// </summary>
        public decimal Total { get; set; }

        public class Contribution
        {
            public string Currency { get; set; }
            public int Divisibility { get; set; }
            public decimal Value { get; set; }
            public decimal CurrencyValue { get; set; }
            public string GroupKey { get; set; }
        }
    }
}
