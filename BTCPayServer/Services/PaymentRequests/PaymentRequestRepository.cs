using System;
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

        public PaymentRequestRepository(ApplicationDbContextFactory contextFactory, InvoiceRepository invoiceRepository)
        {
            _ContextFactory = contextFactory;
            _InvoiceRepository = invoiceRepository;
        }

        public async Task<PaymentRequestData> CreateOrUpdatePaymentRequest(PaymentRequestData entity)
        {
            await using var context = _ContextFactory.CreateContext();
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

        public async Task<PaymentRequestData> FindPaymentRequest(string id, string userId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            using var context = _ContextFactory.CreateContext();
            var result = await context.PaymentRequests.Include(x => x.StoreData)
                .Where(data =>
                    string.IsNullOrEmpty(userId) ||
                    (data.StoreData != null && data.StoreData.UserStores.Any(u => u.ApplicationUserId == userId)))
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            return result;
        }

        public async Task<bool> IsPaymentRequestAdmin(string paymentRequestId, string userId)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(paymentRequestId))
            {
                return false;
            }
            using var context = _ContextFactory.CreateContext();
            return await context.PaymentRequests.Include(x => x.StoreData)
                .AnyAsync(data =>
                    data.Id == paymentRequestId &&
                    (data.StoreData != null && data.StoreData.UserStores.Any(u => u.ApplicationUserId == userId)));
        }

        public async Task UpdatePaymentRequestStatus(string paymentRequestId, Client.Models.PaymentRequestData.PaymentRequestStatus status, CancellationToken cancellationToken = default)
        {
            using var context = _ContextFactory.CreateContext();
            var invoiceData = await context.FindAsync<PaymentRequestData>(paymentRequestId);
            if (invoiceData == null)
                return;
            invoiceData.Status = status;
            await context.SaveChangesAsync(cancellationToken);
        }

        public async Task<PaymentRequestData[]> FindPaymentRequests(PaymentRequestQuery query, CancellationToken cancellationToken = default)
        {
            using var context = _ContextFactory.CreateContext();
            var queryable = context.PaymentRequests.Include(data => data.StoreData).AsQueryable();

            if (!query.IncludeArchived)
            {
                queryable = queryable.Where(data => !data.Archived);
            }
            if (!string.IsNullOrEmpty(query.StoreId))
            {
                queryable = queryable.Where(data =>
                   data.StoreDataId == query.StoreId);
            }

            if (query.Status != null && query.Status.Any())
            {
                queryable = queryable.Where(data =>
                    query.Status.Contains(data.Status));
            }

            if (query.Ids != null && query.Ids.Any())
            {
                queryable = queryable.Where(data =>
                    query.Ids.Contains(data.Id));
            }

            if (!string.IsNullOrEmpty(query.UserId))
            {
                queryable = queryable.Where(i =>
                    i.StoreData != null && i.StoreData.UserStores.Any(u => u.ApplicationUserId == query.UserId));
            }

            queryable = queryable.OrderByDescending(u => u.Created);

            if (query.Skip.HasValue)
            {
                queryable = queryable.Skip(query.Skip.Value);
            }

            if (query.Count.HasValue)
            {
                queryable = queryable.Take(query.Count.Value);
            }
            var items = await queryable.ToArrayAsync(cancellationToken);
            return items;
        }

        public async Task<InvoiceEntity[]> GetInvoicesForPaymentRequest(string paymentRequestId,
            InvoiceQuery invoiceQuery = null)
        {
            if (invoiceQuery == null)
            {
                invoiceQuery = new InvoiceQuery();
            }

            invoiceQuery.OrderId = new[] { GetOrderIdForPaymentRequest(paymentRequestId) };
            return (await _InvoiceRepository.GetInvoices(invoiceQuery))
                .Where(i => i.InternalTags.Contains(GetInternalTag(paymentRequestId)))
                .ToArray();
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
        public bool IncludeArchived { get; set; } = true;
        public Client.Models.PaymentRequestData.PaymentRequestStatus[] Status { get; set; }
        public string UserId { get; set; }
        public int? Skip { get; set; }
        public int? Count { get; set; }
        public string[] Ids { get; set; }
    }
}
