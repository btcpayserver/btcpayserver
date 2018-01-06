using DBreeze;
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


        Network _Network;
        public Network Network
        {
            get
            {
                return _Network;
            }
            set
            {
                _Network = value;
            }
        }

        private ApplicationDbContextFactory _ContextFactory;
        private CustomThreadPool _IndexerThread;
        public InvoiceRepository(ApplicationDbContextFactory contextFactory, string dbreezePath, Network network)
        {
            _Engine = new DBreezeEngine(dbreezePath);
            _IndexerThread = new CustomThreadPool(1, "Invoice Indexer");
            _Network = network;
            _ContextFactory = contextFactory;
        }

        public async Task<bool> RemovePendingInvoice(string invoiceId)
        {
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

        public async Task<string> GetInvoiceIdFromScriptPubKey(Script scriptPubKey)
        {
            using (var db = _ContextFactory.CreateContext())
            {
                var result = await db.AddressInvoices.FindAsync(scriptPubKey.Hash.ToString());
                return result?.InvoiceDataId;
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
            invoice = Clone(invoice);
            invoice.Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16));
            invoice.Payments = new List<PaymentEntity>();
            invoice.StoreId = storeId;
            using (var context = _ContextFactory.CreateContext())
            {
                context.Invoices.Add(new InvoiceData()
                {
                    StoreDataId = storeId,
                    Id = invoice.Id,
                    Created = invoice.InvoiceTime,
                    Blob = ToBytes(invoice),
                    OrderId = invoice.OrderId,
                    Status = invoice.Status,
                    ItemCode = invoice.ProductInformation.ItemCode,
                    CustomerEmail = invoice.RefundMail
                });

                foreach (var cryptoData in invoice.GetCryptoData().Values)
                {
                    var network = networkProvider.GetNetwork(cryptoData.CryptoCode);
                    if (network == null)
                        throw new InvalidOperationException("CryptoCode unsupported");
                    context.AddressInvoices.Add(new AddressInvoiceData()
                    {
                        Address = BitcoinAddress.Create(cryptoData.DepositAddress, network.NBitcoinNetwork).ScriptPubKey.Hash.ToString(),
                        InvoiceDataId = invoice.Id,
                        CreatedTime = DateTimeOffset.UtcNow,
                    });
                    context.HistoricalAddressInvoices.Add(new HistoricalAddressInvoiceData()
                    {
                        InvoiceDataId = invoice.Id,
                        Address = cryptoData.DepositAddress,
#pragma warning disable CS0618
                        CryptoCode = cryptoData.CryptoCode,
#pragma warning restore CS0618
                        Assigned = DateTimeOffset.UtcNow
                    });
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
            textSearch.Add(ToString(invoice.BuyerInformation));
            textSearch.Add(ToString(invoice.ProductInformation));
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

                var invoiceEntity = ToObject<InvoiceEntity>(invoice.Blob);
                var cryptoData = invoiceEntity.GetCryptoData();
                var currencyData = cryptoData.Where(c => c.Value.CryptoCode == network.CryptoCode).Select(f => f.Value).FirstOrDefault();
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
#pragma warning disable CS0618
                invoiceEntity.SetCryptoData(cryptoData);
                invoice.Blob = ToBytes(invoiceEntity);

                context.AddressInvoices.Add(new AddressInvoiceData() { Address = bitcoinAddress.ScriptPubKey.Hash.ToString(), InvoiceDataId = invoiceId, CreatedTime = DateTimeOffset.UtcNow });
                context.HistoricalAddressInvoices.Add(new HistoricalAddressInvoiceData()
                {
                    InvoiceDataId = invoiceId,
                    Address = bitcoinAddress.ToString(),
                    Assigned = DateTimeOffset.UtcNow
                });

                await context.SaveChangesAsync();
                AddToTextSearch(invoice.Id, bitcoinAddress.ToString());
                return true;
            }
        }

        private static void MarkUnassigned(string invoiceId, InvoiceEntity entity, ApplicationDbContext context, string cryptoCode)
        {
            foreach (var address in entity.GetCryptoData())
            {
                if (cryptoCode != null && cryptoCode != address.Value.CryptoCode)
                    continue;
                var historical = new HistoricalAddressInvoiceData();
                historical.InvoiceDataId = invoiceId;
                historical.Address = address.Value.DepositAddress;
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
                var invoiceEntity = ToObject<InvoiceEntity>(invoiceData.Blob);
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
            var entity = ToObject<InvoiceEntity>(invoice.Blob);
            entity.Payments = invoice.Payments.Select(p =>
            {
                var paymentEntity = ToObject<PaymentEntity>(p.Blob);
                paymentEntity.Accounted = p.Accounted;
                return paymentEntity;
            }).ToList();
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
                entity.AvailableAddressHashes = invoice.AddressInvoices.Select(a => a.Address).ToHashSet();
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
                        return new InvoiceEntity[0];
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

        public async Task AddRefundsAsync(string invoiceId, TxOut[] outputs)
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
                        Blob = ToBytes(output)
                    });
                    i++;
                }
                await context.SaveChangesAsync().ConfigureAwait(false);
            }

            var addresses = outputs.Select(o => o.ScriptPubKey.GetDestinationAddress(_Network)).Where(a => a != null).ToArray();
            AddToTextSearch(invoiceId, addresses.Select(a => a.ToString()).ToArray());
        }

        public async Task<PaymentEntity> AddPayment(string invoiceId, Coin receivedCoin)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                PaymentEntity entity = new PaymentEntity
                {
                    Outpoint = receivedCoin.Outpoint,
                    Output = receivedCoin.TxOut,
                    ReceivedTime = DateTime.UtcNow
                };

                PaymentData data = new PaymentData
                {
                    Id = receivedCoin.Outpoint.ToString(),
                    Blob = ToBytes(entity),
                    InvoiceDataId = invoiceId
                };

                context.Payments.Add(data);

                await context.SaveChangesAsync().ConfigureAwait(false);
                AddToTextSearch(invoiceId, receivedCoin.Outpoint.Hash.ToString());
                return entity;
            }
        }

        public async Task UpdatePayments(List<AccountedPaymentEntity> payments)
        {
            if (payments.Count == 0)
                return;
            using (var context = _ContextFactory.CreateContext())
            {
                foreach (var payment in payments)
                {
                    var data = new PaymentData();
                    data.Id = payment.Payment.Outpoint.ToString();
                    data.Accounted = payment.Payment.Accounted;
                    context.Attach(data);
                    context.Entry(data).Property(o => o.Accounted).IsModified = true;
                }
                await context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        private T ToObject<T>(byte[] value)
        {
            return NBitcoin.JsonConverters.Serializer.ToObject<T>(ZipUtils.Unzip(value), Network);
        }

        private byte[] ToBytes<T>(T obj)
        {
            return ZipUtils.Zip(NBitcoin.JsonConverters.Serializer.ToString(obj));
        }

        private T Clone<T>(T invoice)
        {
            return NBitcoin.JsonConverters.Serializer.ToObject<T>(ToString(invoice), Network);
        }

        private string ToString<T>(T data)
        {
            return NBitcoin.JsonConverters.Serializer.ToString(data, Network);
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
    }
}
