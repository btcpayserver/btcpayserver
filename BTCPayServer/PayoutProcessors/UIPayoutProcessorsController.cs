using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.PayoutProcessors;

public class UIPayoutProcessorsController : Controller
{
    private readonly EventAggregator _eventAggregator;
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    private readonly IEnumerable<IPayoutProcessorFactory> _payoutProcessorFactories;
    private readonly PayoutProcessorService _payoutProcessorService;

    public UIPayoutProcessorsController(
        EventAggregator eventAggregator,
        BTCPayNetworkProvider btcPayNetworkProvider,
        IEnumerable<IPayoutProcessorFactory> payoutProcessorFactories,
        PayoutProcessorService payoutProcessorService)
    {
        _eventAggregator = eventAggregator;
        _btcPayNetworkProvider = btcPayNetworkProvider;
        _payoutProcessorFactories = payoutProcessorFactories;
        _payoutProcessorService = payoutProcessorService;
        ;
    }

    [HttpGet("~/stores/{storeId}/payout-processors")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ConfigureStorePayoutProcessors(string storeId)
    {
        var activeProcessors =
            (await _payoutProcessorService.GetProcessors(
                new PayoutProcessorService.PayoutProcessorQuery() { Stores = new[] { storeId } }))
            .GroupBy(data => data.Processor);

        var paymentMethods = HttpContext.GetStoreData().GetEnabledPaymentMethods(_btcPayNetworkProvider)
            .Select(method => method.PaymentId).ToList();

        return View(_payoutProcessorFactories.Select(factory =>
        {
            var conf = activeProcessors.FirstOrDefault(datas => datas.Key == factory.Processor)
                           ?.ToDictionary(data => data.GetPaymentMethodId(), data => data) ??
                       new Dictionary<PaymentMethodId, PayoutProcessorData>();
            foreach (PaymentMethodId supportedPaymentMethod in factory.GetSupportedPaymentMethods())
            {
                conf.TryAdd(supportedPaymentMethod, null);
            }

            return new StorePayoutProcessorsView() { Factory = factory, Configured = conf };
        }).ToList());
    }

    [HttpPost("~/stores/{storeId}/payout-processors/{id}/remove")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Remove(string storeId, string id)
    {
        var tcs = new TaskCompletionSource();
        _eventAggregator.Publish(new PayoutProcessorUpdated()
        {
            Data = null,
            Id = id,
            Processed = tcs
        });
        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Message = "Payout Processor removed"
        });
        await tcs.Task;
        return RedirectToAction("ConfigureStorePayoutProcessors", new { storeId });

    }

    public class StorePayoutProcessorsView
    {
        public Dictionary<PaymentMethodId, PayoutProcessorData> Configured { get; set; }
        public IPayoutProcessorFactory Factory { get; set; }
    }
}
