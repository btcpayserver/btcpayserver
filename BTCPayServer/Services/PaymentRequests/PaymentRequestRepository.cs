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
    public record PaymentRequestEvent
    {
        public const string Created = nameof(Created);
        public const string Updated = nameof(Updated);
        public const string Archived = nameof(Archived);
        public const string StatusChanged = nameof(StatusChanged);
        public PaymentRequestData Data { get; set; }
        public string Type { get; set; }
    }
    
    public class PaymentRequestRepository
    {
        private readonly ApplicationDbContextFactory _ContextFactory;
        private readonly InvoiceRepository _InvoiceRepository;
        private readonly EventAggregator _eventAggregator;

        public PaymentRequestRepository(ApplicationDbContextFactory contextFactory, 
            InvoiceRepository invoiceRepository, EventAggregator eventAggregator)
        {
            _ContextFactory = contextFactory;
            _InvoiceRepository = invoiceRepository;
            _eventAggregator = eventAggregator;
        }

        public async Task<PaymentRequestData> CreateOrUpdatePaymentRequest(PaymentRequestData entity)
        {
            await using var context = _ContextFactory.CreateContext();
            var added = false;
            if (string.IsNullOrEmpty(entity.Id))
            {
                entity.Id = Guid.NewGuid().ToString();
                await context.PaymentRequests.AddAsync(entity);
                added = true;
            }
            else
            {
                context.PaymentRequests.Update(entity);
            }

            await context.SaveChangesAsync();
            _eventAggregator.Publish(new PaymentRequestEvent()
            {
                Data = entity,
                Type = added ? PaymentRequestEvent.Created : PaymentRequestEvent.Updated
            });
            return entity;
        }

        public async Task<bool?> ArchivePaymentRequest(string id, bool toggle = false)
        {
            await using var context = _ContextFactory.CreateContext();
            var pr = await context.PaymentRequests.FindAsync(id);
            if(pr == null)
                return null;
            if(pr.Archived && !toggle)
                return pr.Archived;
            pr.Archived =  !pr.Archived; 
            await context.SaveChangesAsync();
            if (pr.Archived)
            {
                _eventAggregator.Publish(new PaymentRequestEvent()
                {
                    Data = pr,
                    Type = PaymentRequestEvent.Archived
                });
            }
            
            return pr.Archived;
        }

        public async Task<PaymentRequestData> FindPaymentRequest(string id, string userId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            await using var context = _ContextFactory.CreateContext();
            var result = await context.PaymentRequests.Include(x => x.StoreData)
                .Where(data =>
                    string.IsNullOrEmpty(userId) ||
                    (data.StoreData != null && data.StoreData.UserStores.Any(u => u.ApplicationUserId == userId)))
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            return result;
        }

        public async Task UpdatePaymentRequestStatus(string paymentRequestId, Client.Models.PaymentRequestData.PaymentRequestStatus status, CancellationToken cancellationToken = default)
        {
            await using var context = _ContextFactory.CreateContext();
            var paymentRequestData = await context.FindAsync<PaymentRequestData>(paymentRequestId);
            if (paymentRequestData == null)
                return;
            if( paymentRequestData.Status ==  status)
                return;
            paymentRequestData.Status = status;
            
            await context.SaveChangesAsync(cancellationToken);
            
            _eventAggregator.Publish(new PaymentRequestEvent()
            {
                Data = paymentRequestData,
                Type = PaymentRequestEvent.StatusChanged
            });
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
