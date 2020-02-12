using DBriize;
using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using NBitpayClient;
using Newtonsoft.Json;
using System.Linq;
using NBitcoin;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Infrastructure.Internal;
using BTCPayServer.Models;
using System.Threading.Tasks;
using BTCPayServer.Data;
using System.Globalization;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using System.Data.Common;
using NBitcoin.Altcoins;
using NBitcoin.Altcoins.Elements;
using Newtonsoft.Json.Linq;
using Encoders = NBitcoin.DataEncoders.Encoders;

namespace BTCPayServer.Services.Invoices
{
    public class InvoiceRepository : IDisposable
    {


        private readonly DBriizeEngine _Engine;
        public DBriizeEngine Engine
        {
            get
            {
                return _Engine;
            }
        }

        private ApplicationDbContextFactory _ContextFactory;
        private readonly BTCPayNetworkProvider _Networks;
        private CustomThreadPool _IndexerThread;

        public InvoiceRepository(ApplicationDbContextFactory contextFactory, string dbreezePath,
            BTCPayNetworkProvider networks)
        {
            int retryCount = 0;
retry:
            try
            {
                _Engine = new DBriizeEngine(dbreezePath);
            }
            catch when (retryCount++ < 5) { goto retry; }
            _IndexerThread = new CustomThreadPool(1, "Invoice Indexer");
            _ContextFactory = contextFactory;
            _Networks = networks.UnfilteredNetworks;
        }

        public InvoiceEntity CreateNewInvoice()
        {
            return new InvoiceEntity()
            {
                Networks = _Networks,
                Version = InvoiceEntity.Lastest_Version,
                InvoiceTime = DateTimeOffset.UtcNow,
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
                return  (await db.AddressInvoices
                    .Include(a => a.InvoiceData.Payments)
                    .Include(a => a.InvoiceData.RefundAddresses)
#pragma warning disable CS0618
                    .Where(a => addresses.Contains(a.Address))
#pragma warning restore CS0618
                    .Select(a => a.InvoiceData)
                    .ToListAsync()).Select(ToEntity);
            }
        }

        public async Task<string[]> GetPendingInvoices(Func<IQueryable<PendingInvoiceData>, IQueryable<PendingInvoiceData>> filter = null )
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                var queryable =  ctx.PendingInvoices.AsQueryable();
                if (filter != null)
                {
                    queryable = filter.Invoke(queryable);
                }
                return await queryable.Select(p => p.Id).ToArrayAsync();
            }
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
                }
                await ctx.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task ExtendInvoiceMonitor(string invoiceId)
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                var invoiceData = await ctx.Invoices.FindAsync(invoiceId);

                var invoice = ToObject(invoiceData.Blob);
                invoice.MonitoringExpiration = invoice.MonitoringExpiration.AddHours(1);
                invoiceData.Blob = ToBytes(invoice, null);

                await ctx.SaveChangesAsync();
            }
        }

        public async Task<InvoiceEntity> CreateInvoiceAsync(string storeId, InvoiceEntity invoice)
        {
            List<string> textSearch = new List<string>();
            invoice = ToObject(ToBytes(invoice));
            invoice.Networks = _Networks;
            invoice.Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16));
#pragma warning disable CS0618
            invoice.Payments = new List<PaymentEntity>();
#pragma warning restore CS0618
            invoice.StoreId = storeId;
            using (var context = _ContextFactory.CreateContext())
            {
                context.Invoices.Add(new Data.InvoiceData()
                {
                    StoreDataId = storeId,
                    Id = invoice.Id,
                    Created = invoice.InvoiceTime,
                    Blob = ToBytes(invoice, null),
                    OrderId = invoice.OrderId,
#pragma warning disable CS0618 // Type or member is obsolete
                    Status = invoice.StatusString,
#pragma warning restore CS0618 // Type or member is obsolete
                    ItemCode = invoice.ProductInformation.ItemCode,
                    CustomerEmail = invoice.RefundMail
                });

                foreach (var paymentMethod in invoice.GetPaymentMethods())
                {
                    if (paymentMethod.Network == null)
                        throw new InvalidOperationException("CryptoCode unsupported");
                    var paymentDestination = paymentMethod.GetPaymentMethodDetails().GetPaymentDestination();

                    string address = GetDestination(paymentMethod);
                    context.AddressInvoices.Add(new AddressInvoiceData()
                    {
                        InvoiceDataId = invoice.Id,
                        CreatedTime = DateTimeOffset.UtcNow,
                    }.Set(address, paymentMethod.GetId()));

                    context.HistoricalAddressInvoices.Add(new HistoricalAddressInvoiceData()
                    {
                        InvoiceDataId = invoice.Id,
                        Assigned = DateTimeOffset.UtcNow
                    }.SetAddress(paymentDestination, paymentMethod.GetId().ToString()));
                    textSearch.Add(paymentDestination);
                    textSearch.Add(paymentMethod.Calculate().TotalDue.ToString());
                }
                context.PendingInvoices.Add(new PendingInvoiceData() { Id = invoice.Id });
                await context.SaveChangesAsync().ConfigureAwait(false);
            }

            textSearch.Add(invoice.Id);
            textSearch.Add(invoice.InvoiceTime.ToString(CultureInfo.InvariantCulture));
            textSearch.Add(invoice.ProductInformation.Price.ToString(CultureInfo.InvariantCulture));
            textSearch.Add(invoice.OrderId);
            textSearch.Add(ToString(invoice.BuyerInformation, null));
            textSearch.Add(ToString(invoice.ProductInformation, null));
            textSearch.Add(invoice.StoreId);

            AddToTextSearch(invoice.Id, textSearch.ToArray());
            return invoice;
        }

        public async Task AddInvoiceLogs(string invoiceId, InvoiceLogs logs)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                foreach (var log in logs.ToList())
                {
                    context.InvoiceEvents.Add(new InvoiceEventData()
                    {
                        InvoiceDataId = invoiceId,
                        Message = log.Log,
                        Timestamp = log.Timestamp,
                        UniqueId = Encoders.Hex.EncodeData(RandomUtils.GetBytes(10))
                    });
                }
                await context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        private string GetDestination(PaymentMethod paymentMethod)
        {
            // For legacy reason, BitcoinLikeOnChain is putting the hashes of addresses in database
            if (paymentMethod.GetId().PaymentType == Payments.PaymentTypes.BTCLike)
            {
                var network = (BTCPayNetwork)paymentMethod.Network;
                return ((Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod)paymentMethod.GetPaymentMethodDetails()).GetDepositAddress(network.NBitcoinNetwork).ScriptPubKey.Hash.ToString();
            }
            ///////////////
            return paymentMethod.GetPaymentMethodDetails().GetPaymentDestination();
        }

        public async Task<bool> NewAddress(string invoiceId, IPaymentMethodDetails paymentMethod, BTCPayNetworkBase network)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var invoice = (await context.Invoices.Where(i => i.Id == invoiceId).ToListAsync()).FirstOrDefault();
                if (invoice == null)
                    return false;

                var invoiceEntity = ToObject(invoice.Blob);
                var currencyData = invoiceEntity.GetPaymentMethod(network, paymentMethod.GetPaymentType());
                if (currencyData == null)
                    return false;

                var existingPaymentMethod = currencyData.GetPaymentMethodDetails();
                if (existingPaymentMethod.GetPaymentDestination() != null)
                {
                    MarkUnassigned(invoiceId, invoiceEntity, context, currencyData.GetId());
                }

                existingPaymentMethod.SetPaymentDestination(paymentMethod.GetPaymentDestination());
                currencyData.SetPaymentMethodDetails(existingPaymentMethod);
#pragma warning disable CS0618
                if (network.IsBTC)
                {
                    invoiceEntity.DepositAddress = currencyData.DepositAddress;
                }
#pragma warning restore CS0618
                invoiceEntity.SetPaymentMethod(currencyData);
                invoice.Blob = ToBytes(invoiceEntity, network);

                context.AddressInvoices.Add(new AddressInvoiceData()
                {
                    InvoiceDataId = invoiceId,
                    CreatedTime = DateTimeOffset.UtcNow
                }
                .Set(GetDestination(currencyData), currencyData.GetId()));
                context.HistoricalAddressInvoices.Add(new HistoricalAddressInvoiceData()
                {
                    InvoiceDataId = invoiceId,
                    Assigned = DateTimeOffset.UtcNow
                }.SetAddress(paymentMethod.GetPaymentDestination(), network.CryptoCode));

                await context.SaveChangesAsync();
                AddToTextSearch(invoice.Id, paymentMethod.GetPaymentDestination());
                return true;
            }
        }

        public async Task AddPendingInvoiceIfNotPresent(string invoiceId)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                if (!context.PendingInvoices.Any(a => a.Id == invoiceId))
                {
                    context.PendingInvoices.Add(new PendingInvoiceData() { Id = invoiceId });
                    await context.SaveChangesAsync();
                }
            }
        }

        public async Task AddInvoiceEvent(string invoiceId, object evt)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                context.InvoiceEvents.Add(new InvoiceEventData()
                {
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
        }

        private static void MarkUnassigned(string invoiceId, InvoiceEntity entity, ApplicationDbContext context, PaymentMethodId paymentMethodId)
        {
            foreach (var address in entity.GetPaymentMethods())
            {
                if (paymentMethodId != null && paymentMethodId != address.GetId())
                    continue;
                var historical = new HistoricalAddressInvoiceData();
                historical.InvoiceDataId = invoiceId;
                historical.SetAddress(address.GetPaymentMethodDetails().GetPaymentDestination(), address.GetId().ToString());
                historical.UnAssigned = DateTimeOffset.UtcNow;
                context.Attach(historical);
                context.Entry(historical).Property(o => o.UnAssigned).IsModified = true;
            }
        }

        public async Task UnaffectAddress(string invoiceId)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var invoiceData = await context.FindAsync<Data.InvoiceData>(invoiceId).ConfigureAwait(false);
                if (invoiceData == null)
                    return;
                var invoiceEntity = ToObject(invoiceData.Blob);
                MarkUnassigned(invoiceId, invoiceEntity, context, null);
                try
                {
                    await context.SaveChangesAsync();
                }
                catch (DbUpdateException) { } //Possibly, it was unassigned before
            }
        }

        private string[] SearchInvoice(string searchTerms)
        {
            using (var tx = _Engine.GetTransaction())
            {
                var terms = searchTerms.Split(null);
                searchTerms = string.Join(' ', terms.Select(t => t.Length > 50 ? t.Substring(0, 50) : t).ToArray());
                return tx.TextSearch("InvoiceSearch").Block(searchTerms)
                    .GetDocumentIDs()
                    .Select(id => Encoders.Base58.EncodeData(id))
                    .ToArray();
            }
        }

        void AddToTextSearch(string invoiceId, params string[] terms)
        {
            _IndexerThread.DoAsync(() =>
            {
                using (var tx = _Engine.GetTransaction())
                {
                    tx.TextAppend("InvoiceSearch", Encoders.Base58.DecodeData(invoiceId), string.Join(" ", terms.Where(t => !String.IsNullOrWhiteSpace(t))));
                    tx.Commit();
                }
            });
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

        public async Task UpdatePaidInvoiceToInvalid(string invoiceId)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var invoiceData = await context.FindAsync<Data.InvoiceData>(invoiceId).ConfigureAwait(false);
                if (invoiceData == null || !invoiceData.GetInvoiceState().CanMarkInvalid())
                    return;
                invoiceData.Status = "invalid";
                invoiceData.ExceptionStatus = "marked";
                await context.SaveChangesAsync().ConfigureAwait(false);
            }
        }
        public async Task UpdatePaidInvoiceToComplete(string invoiceId)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var invoiceData = await context.FindAsync<Data.InvoiceData>(invoiceId).ConfigureAwait(false);
                if (invoiceData == null || !invoiceData.GetInvoiceState().CanMarkComplete())
                    return;
                invoiceData.Status = "complete";
                invoiceData.ExceptionStatus = "marked";
                await context.SaveChangesAsync().ConfigureAwait(false);
            }
        }
        public async Task<InvoiceEntity> GetInvoice(string id, bool inludeAddressData = false)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                IQueryable<Data.InvoiceData> query =
                    context
                    .Invoices
                    .Include(o => o.Payments)
                    .Include(o => o.RefundAddresses);
                if (inludeAddressData)
                    query = query.Include(o => o.HistoricalAddressInvoices).Include(o => o.AddressInvoices);
                query = query.Where(i => i.Id == id);

                var invoice = (await query.ToListAsync()).FirstOrDefault();
                if (invoice == null)
                    return null;

                return ToEntity(invoice);
            }
        }

        private InvoiceEntity ToEntity(Data.InvoiceData invoice)
        {
            var entity = ToObject(invoice.Blob);
            PaymentMethodDictionary paymentMethods = null;
#pragma warning disable CS0618
            entity.Payments = invoice.Payments.Select(p =>
            {
                var unziped = ZipUtils.Unzip(p.Blob);
                var cryptoCode = GetCryptoCode(unziped);
                var network = _Networks.GetNetwork<BTCPayNetworkBase>(cryptoCode);
                PaymentEntity paymentEntity = null;
                if (network == null)
                {
                    paymentEntity = NBitcoin.JsonConverters.Serializer.ToObject<PaymentEntity>(unziped, null);
                }
                else
                {
                    paymentEntity = network.ToObject<PaymentEntity>(unziped);
                }
                paymentEntity.Network = network;
                paymentEntity.Accounted = p.Accounted;
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
            .OrderBy(a => a.ReceivedTime).ToList();
#pragma warning restore CS0618
            var state = invoice.GetInvoiceState();
            entity.ExceptionStatus = state.ExceptionStatus;
            entity.Status = state.Status;
            entity.RefundMail = invoice.CustomerEmail;
            entity.Refundable = invoice.RefundAddresses.Count != 0;
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

            if (!string.IsNullOrEmpty(entity.RefundMail) && string.IsNullOrEmpty(entity.BuyerInformation.BuyerEmail))
            {
                entity.BuyerInformation.BuyerEmail = entity.RefundMail;
            }
            return entity;
        }

        private string GetCryptoCode(string json)
        {
            if (JObject.Parse(json).TryGetValue("cryptoCode", out var v) && v.Type == JTokenType.String)
                return v.Value<string>();
            return "BTC";
        }

        private IQueryable<Data.InvoiceData> GetInvoiceQuery(ApplicationDbContext context, InvoiceQuery queryObject)
        {
            IQueryable<Data.InvoiceData> query = context.Invoices;

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

            if (queryObject.UserId != null)
            {
                query = query.Where(i => i.StoreData.UserStores.Any(u => u.ApplicationUserId == queryObject.UserId));
            }

            if (!string.IsNullOrEmpty(queryObject.TextSearch))
            {
                var ids = new HashSet<string>(SearchInvoice(queryObject.TextSearch)).ToArray();
                if (ids.Length == 0)
                {
                    // Hacky way to return an empty query object. The nice way is much too elaborate:
                    // https://stackoverflow.com/questions/33305495/how-to-return-empty-iqueryable-in-an-async-repository-method
                    return query.Where(x => false);
                }
                query = query.Where(i => ids.Contains(i.Id));
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

            if (queryObject.Count != null)
                query = query.Take(queryObject.Count.Value);

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
                query = query.Include(o => o.Payments)
                    .Include(o => o.RefundAddresses);
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

        public async Task AddRefundsAsync(string invoiceId, TxOut[] outputs, BTCPayNetwork network)
        {
            if (outputs.Length == 0)
                return;
            outputs = outputs.Take(10).ToArray();
            using (var context = _ContextFactory.CreateContext())
            {
                int i = 0;
                foreach (var output in outputs)
                {
                    context.RefundAddresses.Add(new RefundAddressesData()
                    {
                        Id = invoiceId + "-" + i,
                        InvoiceDataId = invoiceId,
                        Blob = ToBytes(output, network)
                    });
                    i++;
                }
                await context.SaveChangesAsync().ConfigureAwait(false);
            }

            var addresses = outputs.Select(o => o.ScriptPubKey.GetDestinationAddress(network.NBitcoinNetwork)).Where(a => a != null).ToArray();
            AddToTextSearch(invoiceId, addresses.Select(a => a.ToString()).ToArray());
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
                InvoiceEntity invoiceEntity = ToObject(invoice.Blob);
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

                context.Payments.Add(data);

                try
                {
                    await context.SaveChangesAsync().ConfigureAwait(false);
                }
                catch (DbUpdateException) { return null; } // Already exists
                AddToTextSearch(invoiceId, paymentData.GetSearchTerms());
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

        private InvoiceEntity ToObject(byte[] value)
        {
            var entity = NBitcoin.JsonConverters.Serializer.ToObject<InvoiceEntity>(ZipUtils.Unzip(value), null);
            entity.Networks = _Networks;
            return entity;
        }

        private byte[] ToBytes<T>(T obj, BTCPayNetworkBase network = null)
        {
            return ZipUtils.Zip(ToString(obj, network));
        }

        private string ToString<T>(T data, BTCPayNetworkBase network)
        {
            if (network == null)
            {
                return NBitcoin.JsonConverters.Serializer.ToString(data, null);
            }
            return network.ToString(data);
        }

        public void Dispose()
        {
            if (_Engine != null)
                _Engine.Dispose();
            if (_IndexerThread != null)
                _IndexerThread.Dispose();
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

        public int? Count
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
    }
}
