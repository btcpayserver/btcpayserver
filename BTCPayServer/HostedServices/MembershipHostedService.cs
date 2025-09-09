using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Blazor.VaultBridge.Elements;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Services.Invoices;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BTCPayServer.HostedServices;

public class MembershipHostedService(
    EventAggregator eventAggregator,
    ApplicationDbContextFactory applicationDbContextFactory,
    Logs logger) : EventHostedServiceBase(eventAggregator, logger)
{
    override protected void SubscribeToEvents()
    {
        this.Subscribe<Events.InvoiceEvent>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is Events.InvoiceEvent
            {
                EventCode: Events.InvoiceEventCode.Completed,
                Invoice:
                {
                    Status: InvoiceStatus.Settled
                } settledInvoice
            } &&
            UIStoreMembershipController.GetPlanIdFromInvoice(settledInvoice) is string planId)
        {
            await ProcessSubscriptionCompletePayment(settledInvoice, planId);
        }
    }

    private async Task ProcessSubscriptionCompletePayment(InvoiceEntity invoice, string planId)
    {
        var custId = await GetOrUpdateCustomerId(invoice.StoreId, invoice.Metadata.BuyerEmail);
        var member = await GetMemberByCustomerId(invoice.StoreId, custId);
    }

    private async Task<object> GetMemberByCustomerId(string invoiceStoreId, string custId)
    {
        throw new System.NotImplementedException();
    }

    private async Task<string> GetOrUpdateCustomerId(string storeId, string email)
    {
        await using var ctx = applicationDbContextFactory.CreateContext();
        var id = CustomerData.GenerateId();
        return await ctx.Database.GetDbConnection().ExecuteScalarAsync<string>
        ("""
         INSERT INTO customers (id, store_id, email) VALUES (@id, @storeId, @email)
         ON CONFLICT (store_id, email) DO NOTHING
         RETURNING id
         """, new { id, storeId, email });
    }
}
