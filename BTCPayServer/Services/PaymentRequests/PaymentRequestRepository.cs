using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Services.PaymentRequests
{
    public class PaymentRequestRepository
    {
        private readonly ApplicationDbContextFactory _ContextFactory;
        private readonly InvoiceRepository _InvoiceRepository;
        private readonly StoreRepository _storeRepository;

        public PaymentRequestRepository(ApplicationDbContextFactory contextFactory, InvoiceRepository invoiceRepository,
            StoreRepository storeRepository)
        {
            _ContextFactory = contextFactory;
            _InvoiceRepository = invoiceRepository;
            _storeRepository = storeRepository;
        }


        public async Task<PaymentRequestData> CreateOrUpdatePaymentRequest(PaymentRequestData entity)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                if (string.IsNullOrEmpty(entity.Id))
                {
                    entity.Id = Guid.NewGuid().ToString();
                    await context.PaymentRequests.AddAsync(entity);
                }
                else
                {
                    context.PaymentRequests.Update(entity);
                }

                await context.SaveChangesAsync();
                return entity;
            }
        }

        public async Task<PaymentRequestData> FindPaymentRequest(string id, string userId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            using (var context = _ContextFactory.CreateContext())
            {
                var result = await context.PaymentRequests.Include(x => x.StoreData)
                    .Where(data =>
                        string.IsNullOrEmpty(userId) ||
                        (data.StoreData != null && data.StoreData.UserStores.Any(u => u.ApplicationUserId == userId)))
                    .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
                return result;
            }
        }

        public async Task<bool> IsPaymentRequestAdmin(string paymentRequestId, string userId)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(paymentRequestId))
            {
                return false;
            }
            using (var context = _ContextFactory.CreateContext())
            {
                return await context.PaymentRequests.Include(x => x.StoreData)
                    .AnyAsync(data =>
                        data.Id == paymentRequestId &&
                        (data.StoreData != null &&  data.StoreData.UserStores.Any(u => u.ApplicationUserId == userId)));
            }
        }
        
        public async Task UpdatePaymentRequestStatus(string paymentRequestId, PaymentRequestData.PaymentRequestStatus status, CancellationToken cancellationToken = default)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var invoiceData = await context.FindAsync<PaymentRequestData>(paymentRequestId);
                if (invoiceData == null)
                    return;
                invoiceData.Status = status;
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task<(int Total, PaymentRequestData[] Items)> FindPaymentRequests(PaymentRequestQuery query, CancellationToken cancellationToken = default)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var queryable = context.PaymentRequests.Include(data => data.StoreData).AsQueryable();
                if (!string.IsNullOrEmpty(query.StoreId))
                {
                    queryable = queryable.Where(data =>
                       data.StoreDataId.Equals(query.StoreId, StringComparison.InvariantCulture));
                }

                if (query.Status != null && query.Status.Any())
                {
                    queryable = queryable.Where(data =>
                        query.Status.Contains(data.Status));
                }

                if (!string.IsNullOrEmpty(query.UserId))
                {
                    queryable = queryable.Where(i =>
                        i.StoreData != null && i.StoreData.UserStores.Any(u => u.ApplicationUserId == query.UserId));
                }

                var total = await queryable.CountAsync(cancellationToken);

                queryable = queryable.OrderByDescending(u => u.Created);

                if (query.Skip.HasValue)
                {
                    queryable = queryable.Skip(query.Skip.Value);
                }

                if (query.Count.HasValue)
                {
                    queryable = queryable.Take(query.Count.Value);
                }
                return (total, await queryable.ToArrayAsync(cancellationToken));
            }
        }

        public async Task<bool> RemovePaymentRequest(string id, string userId)
        {
            using (var context = _ContextFactory.CreateContext())
            {
                var canDelete = !(await GetInvoicesForPaymentRequest(id)).Any();
                if (!canDelete) return false;
                var pr = await FindPaymentRequest(id, userId);
                if (pr == null)
                {
                    return false;
                }

                context.PaymentRequests.Remove(pr);
                await context.SaveChangesAsync();

                return true;
            }
        }

        public async Task<InvoiceEntity[]> GetInvoicesForPaymentRequest(string paymentRequestId,
            InvoiceQuery invoiceQuery = null)
        {
            if (invoiceQuery == null)
            {
                invoiceQuery = new InvoiceQuery();
            }

            invoiceQuery.OrderId = new[] {GetOrderIdForPaymentRequest(paymentRequestId)};
            return await _InvoiceRepository.GetInvoices(invoiceQuery);
        }

        public static string GetOrderIdForPaymentRequest(string paymentRequestId)
        {
            return $"PAY_REQUEST_{paymentRequestId}";
        }

        public static string GetPaymentRequestIdFromOrderId(string invoiceOrderId)
        {
            if (string.IsNullOrEmpty(invoiceOrderId) ||
                !invoiceOrderId.StartsWith("PAY_REQUEST_", StringComparison.InvariantCulture))
            {
                return null;
            }

            return invoiceOrderId.Replace("PAY_REQUEST_", "", StringComparison.InvariantCulture);
        }

        public static string GetInternalTag(string id)
        {
            return $"PAYREQ#{id}";
        }
        public static string[] GetPaymentIdsFromInternalTags(InvoiceEntity invoiceEntity)
        {
            return invoiceEntity.GetInternalTags("PAYREQ#");
        }
    }

    public class PaymentRequestUpdated
    {
        public string PaymentRequestId { get; set; }
        public PaymentRequestData Data { get; set; }
    }

    public class PaymentRequestQuery
    {
        public string StoreId { get; set; }
        
        public PaymentRequestData.PaymentRequestStatus[] Status{ get; set; }
        public string UserId { get; set; }
        public int? Skip { get; set; }
        public int? Count { get; set; }
    }
}
