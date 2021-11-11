using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.TransferProcessors.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PayoutData = BTCPayServer.Data.PayoutData;

namespace BTCPayServer.TransferProcessors;

public abstract class BaseTransferSender<T> : BaseAsyncService where T:AutomatedTransferBlob
{
    protected readonly StoreRepository _storeRepository;
    protected readonly TransferProcessorData TransferProcesserSettings;
    protected readonly ApplicationDbContextFactory _applicationDbContextFactory;
    protected readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    protected readonly PaymentMethodId PaymentMethodId;

    protected BaseTransferSender(
        ILoggerFactory logger,
        StoreRepository storeRepository,
        TransferProcessorData transferProcesserSettings,
        ApplicationDbContextFactory applicationDbContextFactory,
        BTCPayNetworkProvider btcPayNetworkProvider) : base(logger.CreateLogger($"{transferProcesserSettings.Processor}:{transferProcesserSettings.StoreId}:{transferProcesserSettings.PaymentMethod}"))
    {
        _storeRepository = storeRepository;
        TransferProcesserSettings = transferProcesserSettings;
        PaymentMethodId = TransferProcesserSettings.GetPaymentMethodId();
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
        Logs.PayServer.LogInformation($"Starting to process");
        var store = await _storeRepository.FindStore(TransferProcesserSettings.StoreId);
        var paymentMethod = store?.GetEnabledPaymentMethods(_btcPayNetworkProvider)?.FirstOrDefault(
            method =>
                method.PaymentId == PaymentMethodId);
        if (paymentMethod is not null)
        {
            var payouts = await GetRelevantPayouts();
            Logs.PayServer.LogInformation($"{payouts.Length} found to process");
            await Process(paymentMethod, payouts);
        }
        else
        {
            Logs.PayServer.LogInformation($"Payment method not configured.");
        }

        var blob = GetBlob(TransferProcesserSettings);

        Logs.PayServer.LogInformation($"Sleeping for {blob.Interval}");
        await Task.Delay(blob.Interval, CancellationToken);
    }


    public static T GetBlob(TransferProcessorData data)
    {
        return InvoiceRepository.FromBytes<T>(data.Blob);
    }

    private async Task<PayoutData[]> GetRelevantPayouts()
    {
        await using var context = _applicationDbContextFactory.CreateContext();
        var pmi = TransferProcesserSettings.PaymentMethod;
        return await context.Payouts
            .Where(data => data.State == PayoutState.AwaitingPayment)
            .Where(data => data.PaymentMethodId == pmi)
            .Where(data => data.StoreDataId == TransferProcesserSettings.StoreId)
            .OrderBy(data => data.Date)
            .ToArrayAsync();
    }
}
