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
            _Engine = new DBreezeEngine(dbreezePath);
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

        public async Task<InvoiceEntity> CreateInvoiceAsync(string storeId, InvoiceEntity invoice, BTCPayNetworkProvider networkProvider)
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
                context.Invoices.Add(new InvoiceData()
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

                foreach (var cryptoData in invoice.GetCryptoData(networkProvider).Values)
                {
                    if (cryptoData.Network == null)
                        throw new InvalidOperationException("CryptoCode unsupported");
                    context.AddressInvoices.Add(new AddressInvoiceData()
                    {
                        InvoiceDataId = invoice.Id,
                        CreatedTime = DateTimeOffset.UtcNow,
                    }.SetHash(cryptoData.GetDepositAddress().ScriptPubKey.Hash, cryptoData.CryptoCode));
                    context.HistoricalAddressInvoices.Add(new HistoricalAddressInvoiceData()
                    {
                        InvoiceDataId = invoice.Id,
                        Assigned = DateTimeOffset.UtcNow
                    }.SetAddress(cryptoData.DepositAddress, cryptoData.CryptoCode));
                    textSearch.Add(cryptoData.DepositAddress);
                    textSearch.Add(cryptoData.Calculate().TotalDue.ToString());
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

        public async Task<bool> NewAddress(string invoiceId, BitcoinAddress bitcoinAddress, BTCPayNetwork network)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var invoice = await context.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId);
                if (invoice == null)
                    return false;

                var invoiceEntity = ToObject<InvoiceEntity>(invoice.Blob, network.NBitcoinNetwork);
                var currencyData = invoiceEntity.GetCryptoData(network, null);
                if (currencyData == null)
                    return false;

                if (currencyData.DepositAddress != null)
                {
                    MarkUnassigned(invoiceId, invoiceEntity, context, network.CryptoCode);
                }

                currencyData.DepositAddress = bitcoinAddress.ToString();

#pragma warning disable CS0618
                if (network.IsBTC)
                {
                    invoiceEntity.DepositAddress = currencyData.DepositAddress;
                }
#pragma warning restore CS0618
                invoiceEntity.SetCryptoData(currencyData);
                invoice.Blob = ToBytes(invoiceEntity, network.NBitcoinNetwork);

                context.AddressInvoices.Add(new AddressInvoiceData()
                {
                    InvoiceDataId = invoiceId,
                    CreatedTime = DateTimeOffset.UtcNow
                }
                .SetHash(bitcoinAddress.ScriptPubKey.Hash, network.CryptoCode));
                context.HistoricalAddressInvoices.Add(new HistoricalAddressInvoiceData()
                {
                    InvoiceDataId = invoiceId,
                    Assigned = DateTimeOffset.UtcNow
                }.SetAddress(bitcoinAddress.ToString(), network.CryptoCode));

                await context.SaveChangesAsync();
                AddToTextSearch(invoice.Id, bitcoinAddress.ToString());
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
                await context.SaveChangesAsync();
            }
        }

        private static void MarkUnassigned(string invoiceId, InvoiceEntity entity, ApplicationDbContext context, string cryptoCode)
        {
            foreach (var address in entity.GetCryptoData(null))
            {
                if (cryptoCode != null && cryptoCode != address.Value.CryptoCode)
                    continue;
                var historical = new HistoricalAddressInvoiceData();
                historical.InvoiceDataId = invoiceId;
                historical.SetAddress(address.Value.DepositAddress, address.Value.CryptoCode);
                historical.UnAssigned = DateTimeOffset.UtcNow;
                context.Attach(historical);
                context.Entry(historical).Property(o => o.UnAssigned).IsModified = true;
            }
        }

        public async Task UnaffectAddress(string invoiceId)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var invoiceData = await context.FindAsync<InvoiceData>(invoiceId).ConfigureAwait(false);
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
                var invoiceData = await context.FindAsync<InvoiceData>(invoiceId).ConfigureAwait(false);
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
                var invoiceData = await context.FindAsync<InvoiceData>(invoiceId).ConfigureAwait(false);
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
                IQueryable<InvoiceData> query =
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

        private InvoiceEntity ToEntity(InvoiceData invoice)
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
                entity.AvailableAddressHashes = invoice.AddressInvoices.Select(a => a.GetHash() + a.GetCryptoCode()).ToHashSet();
            }
            if(invoice.Events != null)
            {
                entity.Events = invoice.Events.OrderBy(c => c.Timestamp).ToList();
            }
            return entity;
        }


        public async Task<InvoiceEntity[]> GetInvoices(InvoiceQuery queryObject)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                IQueryable<InvoiceData> query = context
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

                if (!string.IsNullOrEmpty(queryObject.StoreId))
                {
                    query = query.Where(i => i.StoreDataId == queryObject.StoreId);
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

                if (queryObject.Status != null)
                    query = query.Where(i => i.Status == queryObject.Status);

                query = query.OrderByDescending(q => q.Created);

                if (queryObject.Skip != null)
                    query = query.Skip(queryObject.Skip.Value);

                if (queryObject.Count != null)
                    query = query.Take(queryObject.Count.Value);

                var data = await query.ToArrayAsync().ConfigureAwait(false);

                return data.Select(ToEntity).ToArray();
            }

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

        public async Task<PaymentEntity> AddPayment(string invoiceId, DateTimeOffset date, Coin receivedCoin, string cryptoCode)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                PaymentEntity entity = new PaymentEntity
                {
                    Outpoint = receivedCoin.Outpoint,
#pragma warning disable CS0618
                    Output = receivedCoin.TxOut,
                    CryptoCode = cryptoCode,
#pragma warning restore CS0618
                    ReceivedTime = date.UtcDateTime,
                    Accounted = false
                };
                entity.SetCryptoPaymentData(new BitcoinLikePaymentData());
                PaymentData data = new PaymentData
                {
                    Id = receivedCoin.Outpoint.ToString(),
                    Blob = ToBytes(entity, null),
                    InvoiceDataId = invoiceId,
                    Accounted = false
                };

                context.Payments.Add(data);

                await context.SaveChangesAsync().ConfigureAwait(false);
                AddToTextSearch(invoiceId, receivedCoin.Outpoint.Hash.ToString());
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
                    var data = new PaymentData();
                    data.Id = payment.Outpoint.ToString();
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
        public string StoreId
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

        public string Status
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
