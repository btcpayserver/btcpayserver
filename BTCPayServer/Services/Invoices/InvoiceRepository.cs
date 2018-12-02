using DBreeze;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using NBitpayClient;
using Newtonsoft.Json;
using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;
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

namespace BTCPayServer.Services.Invoices
{
    public class InvoiceRepository : IDisposable
    {


        private readonly DBreezeEngine _Engine;
        public DBreezeEngine Engine
        {
            get
            {
                return _Engine;
            }
        }

        private ApplicationDbContextFactory _ContextFactory;
        private CustomThreadPool _IndexerThread;
        public InvoiceRepository(ApplicationDbContextFactory contextFactory, string dbreezePath)
        {
            int retryCount = 0;
            retry:
            try
            {
                _Engine = new DBreezeEngine(dbreezePath);
            }
            catch when (retryCount++ < 5) { goto retry; }
            _IndexerThread = new CustomThreadPool(1, "Invoice Indexer");
            _ContextFactory = contextFactory;
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

        public async Task<InvoiceEntity> GetInvoiceFromScriptPubKey(Script scriptPubKey, string cryptoCode)
        {
            using (var db = _ContextFactory.CreateContext())
            {
                var key = scriptPubKey.Hash.ToString() + "#" + cryptoCode;
                var result = await db.AddressInvoices
#pragma warning disable CS0618
                                    .Where(a => a.Address == key)
#pragma warning restore CS0618
                                    .Select(a => a.InvoiceData)
                                    .Include(a => a.Payments)
                                    .Include(a => a.RefundAddresses)
                                    .FirstOrDefaultAsync();
                if (result == null)
                    return null;
                return ToEntity(result);
            }
        }

        public async Task<string[]> GetPendingInvoices()
        {
            using (var ctx = _ContextFactory.CreateContext())
            {
                return await ctx.PendingInvoices.Select(p => p.Id).ToArrayAsync();
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

        public async Task<InvoiceEntity> CreateInvoiceAsync(string storeId, InvoiceEntity invoice, InvoiceLogs creationLogs, BTCPayNetworkProvider networkProvider)
        {
            List<string> textSearch = new List<string>();
            invoice = Clone(invoice, null);
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
                    Status = invoice.Status,
                    ItemCode = invoice.ProductInformation.ItemCode,
                    CustomerEmail = invoice.RefundMail
                });

                foreach (var paymentMethod in invoice.GetPaymentMethods(networkProvider))
                {
                    if (paymentMethod.Network == null)
                        throw new InvalidOperationException("CryptoCode unsupported");
                    var paymentDestination = paymentMethod.GetPaymentMethodDetails().GetPaymentDestination();

                    string address = GetDestination(paymentMethod, paymentMethod.Network.NBitcoinNetwork);
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

                foreach (var log in creationLogs.ToList())
                {
                    context.InvoiceEvents.Add(new InvoiceEventData()
                    {
                        InvoiceDataId = invoice.Id,
                        Message = log.Log,
                        Timestamp = log.Timestamp,
                        UniqueId = Encoders.Hex.EncodeData(RandomUtils.GetBytes(10))
                    });
                }
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

        private static string GetDestination(PaymentMethod paymentMethod, Network network)
        {
            // For legacy reason, BitcoinLikeOnChain is putting the hashes of addresses in database
            if (paymentMethod.GetId().PaymentType == Payments.PaymentTypes.BTCLike)
            {
                return ((Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod)paymentMethod.GetPaymentMethodDetails()).GetDepositAddress(network).ScriptPubKey.Hash.ToString();
            }
            ///////////////
            return paymentMethod.GetPaymentMethodDetails().GetPaymentDestination();
        }

        public async Task<bool> NewAddress(string invoiceId, IPaymentMethodDetails paymentMethod, BTCPayNetwork network)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var invoice = await context.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId);
                if (invoice == null)
                    return false;

                var invoiceEntity = ToObject<InvoiceEntity>(invoice.Blob, network.NBitcoinNetwork);
                var currencyData = invoiceEntity.GetPaymentMethod(network, paymentMethod.GetPaymentType(), null);
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
                invoice.Blob = ToBytes(invoiceEntity, network.NBitcoinNetwork);

                context.AddressInvoices.Add(new AddressInvoiceData()
                {
                    InvoiceDataId = invoiceId,
                    CreatedTime = DateTimeOffset.UtcNow
                }
                .Set(GetDestination(currencyData, network.NBitcoinNetwork), currencyData.GetId()));
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
            foreach (var address in entity.GetPaymentMethods(null))
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
                var invoiceEntity = ToObject<InvoiceEntity>(invoiceData.Blob, null);
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

        public async Task UpdateInvoiceStatus(string invoiceId, string status, string exceptionStatus)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var invoiceData = await context.FindAsync<Data.InvoiceData>(invoiceId).ConfigureAwait(false);
                if (invoiceData == null)
                    return;
                invoiceData.Status = status;
                invoiceData.ExceptionStatus = exceptionStatus;
                await context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdatePaidInvoiceToInvalid(string invoiceId)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var invoiceData = await context.FindAsync<Data.InvoiceData>(invoiceId).ConfigureAwait(false);
                if (invoiceData?.Status != "paid")
                    return;
                invoiceData.Status = "invalid";
                await context.SaveChangesAsync().ConfigureAwait(false);
            }
        }
        public async Task<InvoiceEntity> GetInvoice(string storeId, string id, bool inludeAddressData = false)
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

                if (storeId != null)
                    query = query.Where(i => i.StoreDataId == storeId);

                var invoice = await query.FirstOrDefaultAsync().ConfigureAwait(false);
                if (invoice == null)
                    return null;

                return ToEntity(invoice);
            }
        }

        private InvoiceEntity ToEntity(Data.InvoiceData invoice)
        {
            var entity = ToObject<InvoiceEntity>(invoice.Blob, null);
#pragma warning disable CS0618
            entity.Payments = invoice.Payments.Select(p =>
            {
                var paymentEntity = ToObject<PaymentEntity>(p.Blob, null);
                paymentEntity.Accounted = p.Accounted;
                return paymentEntity;
            }).ToList();
#pragma warning restore CS0618
            entity.ExceptionStatus = invoice.ExceptionStatus;
            entity.Status = invoice.Status;
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
            return entity;
        }


        public async Task<InvoiceEntity[]> GetInvoices(InvoiceQuery queryObject)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                IQueryable<Data.InvoiceData> query = context
                    .Invoices
                    .Include(o => o.Payments)
                    .Include(o => o.RefundAddresses);
                if (queryObject.IncludeAddresses)
                    query = query.Include(o => o.HistoricalAddressInvoices).Include(o => o.AddressInvoices);
                if (queryObject.IncludeEvents)
                    query = query.Include(o => o.Events);
                if (!string.IsNullOrEmpty(queryObject.InvoiceId))
                {
                    query = query.Where(i => i.Id == queryObject.InvoiceId);
                }

                if (queryObject.StoreId != null && queryObject.StoreId.Length > 0)
                {
                    var stores = queryObject.StoreId.ToHashSet();
                    query = query.Where(i => stores.Contains(i.StoreDataId));
                }

                if (queryObject.UserId != null)
                {
                    query = query.Where(i => i.StoreData.UserStores.Any(u => u.ApplicationUserId == queryObject.UserId));
                }

                if (!string.IsNullOrEmpty(queryObject.TextSearch))
                {
                    var ids = new HashSet<string>(SearchInvoice(queryObject.TextSearch));
                    if (ids.Count == 0)
                        return Array.Empty<InvoiceEntity>();
                    query = query.Where(i => ids.Contains(i.Id));
                }

                if (queryObject.StartDate != null)
                    query = query.Where(i => queryObject.StartDate.Value <= i.Created);

                if (queryObject.EndDate != null)
                    query = query.Where(i => i.Created <= queryObject.EndDate.Value);

                if (queryObject.ItemCode != null)
                    query = query.Where(i => i.ItemCode == queryObject.ItemCode);

                if (queryObject.OrderId != null)
                    query = query.Where(i => i.OrderId == queryObject.OrderId);

                if (queryObject.Status != null && queryObject.Status.Length > 0)
                {
                    var statusSet = queryObject.Status.ToHashSet();
                    query = query.Where(i => statusSet.Contains(i.Status));
                }

                if (queryObject.Unusual != null)
                {
                    var unused = queryObject.Unusual.Value;
                    query = query.Where(i => unused == (i.Status == "invalid" || i.ExceptionStatus != null));
                }

                if (queryObject.ExceptionStatus != null && queryObject.ExceptionStatus.Length > 0)
                {
                    var exceptionStatusSet = queryObject.ExceptionStatus.Select(s => NormalizeExceptionStatus(s)).ToHashSet();
                    query = query.Where(i => exceptionStatusSet.Contains(i.ExceptionStatus));
                }

                query = query.OrderByDescending(q => q.Created);

                if (queryObject.Skip != null)
                    query = query.Skip(queryObject.Skip.Value);

                if (queryObject.Count != null)
                    query = query.Take(queryObject.Count.Value);

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

        public async Task AddRefundsAsync(string invoiceId, TxOut[] outputs, Network network)
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

            var addresses = outputs.Select(o => o.ScriptPubKey.GetDestinationAddress(network)).Where(a => a != null).ToArray();
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
        public async Task<PaymentEntity> AddPayment(string invoiceId, DateTimeOffset date, CryptoPaymentData paymentData, string cryptoCode, bool accounted = false)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                PaymentEntity entity = new PaymentEntity
                {
#pragma warning disable CS0618
                    CryptoCode = cryptoCode,
#pragma warning restore CS0618
                    ReceivedTime = date.UtcDateTime,
                    Accounted = accounted
                };
                entity.SetCryptoPaymentData(paymentData);


                PaymentData data = new PaymentData
                {
                    Id = paymentData.GetPaymentId(),
                    Blob = ToBytes(entity, null),
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
                    data.Blob = ToBytes(payment, null);
                    context.Attach(data);
                    context.Entry(data).Property(o => o.Accounted).IsModified = true;
                    context.Entry(data).Property(o => o.Blob).IsModified = true;
                }
                await context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        private T ToObject<T>(byte[] value, Network network)
        {
            return NBitcoin.JsonConverters.Serializer.ToObject<T>(ZipUtils.Unzip(value), network);
        }

        private byte[] ToBytes<T>(T obj, Network network)
        {
            return ZipUtils.Zip(NBitcoin.JsonConverters.Serializer.ToString(obj, network));
        }

        private T Clone<T>(T invoice, Network network)
        {
            return NBitcoin.JsonConverters.Serializer.ToObject<T>(ToString(invoice, network), network);
        }

        private string ToString<T>(T data, Network network)
        {
            return NBitcoin.JsonConverters.Serializer.ToString(data, network);
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

        public string OrderId
        {
            get; set;
        }

        public string ItemCode
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

        public string InvoiceId
        {
            get;
            set;
        }
        public bool IncludeAddresses { get; set; }

        public bool IncludeEvents { get; set; }
    }
}
