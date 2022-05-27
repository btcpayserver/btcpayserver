using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Data.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.PayoutProcessors.Settings;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayoutData = BTCPayServer.Data.PayoutData;
using PayoutProcessorData = BTCPayServer.Data.Data.PayoutProcessorData;

namespace BTCPayServer.PayoutProcessors;

public abstract class BaseAutomatedPayoutProcessor<T> : BaseAsyncService where T:AutomatedPayoutBlob
{
    protected readonly StoreRepository _storeRepository;
    protected readonly PayoutProcessorData _PayoutProcesserSettings;
    protected readonly ApplicationDbContextFactory _applicationDbContextFactory;
    protected readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    protected readonly PaymentMethodId PaymentMethodId;

    protected BaseAutomatedPayoutProcessor(
        ILoggerFactory logger,
        StoreRepository storeRepository,
        PayoutProcessorData payoutProcesserSettings,
        ApplicationDbContextFactory applicationDbContextFactory,
        BTCPayNetworkProvider btcPayNetworkProvider) : base(logger.CreateLogger($"{payoutProcesserSettings.Processor}:{payoutProcesserSettings.StoreId}:{payoutProcesserSettings.PaymentMethod}"))
    {
        _storeRepository = storeRepository;
        _PayoutProcesserSettings = payoutProcesserSettings;
        PaymentMethodId = _PayoutProcesserSettings.GetPaymentMethodId();
        _applicationDbContextFactory = applicationDbContextFactory;
        _btcPayNetworkProvider = btcPayNetworkProvider;
    }

    internal override Task[] InitializeTasks()
    {
        return new[] { CreateLoopTask(Act) };
    }

    protected abstract Task Process(ISupportedPaymentMethod paymentMethod, PayoutData[] payouts);

    private async Task Act()
    {
        var store = await _storeRepository.FindStore(_PayoutProcesserSettings.StoreId);
        var paymentMethod = store?.GetEnabledPaymentMethods(_btcPayNetworkProvider)?.FirstOrDefault(
            method =>
                method.PaymentId == PaymentMethodId);
        
        var blob = GetBlob(_PayoutProcesserSettings);
        if (paymentMethod is not null)
        {
            var payouts = await GetRelevantPayouts();
            if (payouts.Length > 0)
            {
                Logs.PayServer.LogInformation($"{payouts.Length} found to process. Starting (and after will sleep for {blob.Interval})");
                await Process(paymentMethod, payouts);
            }
        }
        await Task.Delay(blob.Interval, CancellationToken);
    }


    public static T GetBlob(PayoutProcessorData data)
    {
        return InvoiceRepository.FromBytes<T>(data.Blob);
    }

    private async Task<PayoutData[]> GetRelevantPayouts()
    {
        await using var context = _applicationDbContextFactory.CreateContext();
        var pmi = _PayoutProcesserSettings.PaymentMethod;
        return await context.Payouts
            .Where(data => data.State == PayoutState.AwaitingPayment)
            .Where(data => data.PaymentMethodId == pmi)
            .Where(data => data.StoreDataId == _PayoutProcesserSettings.StoreId)
            .OrderBy(data => data.Date)
            .ToArrayAsync();
    }
}
