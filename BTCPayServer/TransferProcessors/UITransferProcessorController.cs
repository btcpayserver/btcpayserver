using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.TransferProcessors;

public class UITransferProcessorsController : Controller
{
    private readonly EventAggregator _eventAggregator;
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    private readonly IEnumerable<ITransferProcessorFactory> _transferProcessorFactories;
    private readonly TransferProcessorService _transferProcessorService;

    public UITransferProcessorsController(
        EventAggregator eventAggregator,
        BTCPayNetworkProvider btcPayNetworkProvider,
        IEnumerable<ITransferProcessorFactory> transferProcessorFactories,
        TransferProcessorService transferProcessorService)
    {
        _eventAggregator = eventAggregator;
        _btcPayNetworkProvider = btcPayNetworkProvider;
        _transferProcessorFactories = transferProcessorFactories;
        _transferProcessorService = transferProcessorService;
        ;
    }

    [HttpGet("~/stores/{storeId}/transfer-processors")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ConfigureStoreTransferProcessors(string storeId)
    {
        var activeProcessors =
            (await _transferProcessorService.GetProcessors(
                new TransferProcessorService.TransferProcessorQuery() { Stores = new[] { storeId } }))
            .GroupBy(data => data.Processor);

        var paymentMethods = HttpContext.GetStoreData().GetEnabledPaymentMethods(_btcPayNetworkProvider)
            .Select(method => method.PaymentId).ToList();

        return View(_transferProcessorFactories.Select(factory =>
        {
            var conf = activeProcessors.FirstOrDefault(datas => datas.Key == factory.Processor)
                           ?.ToDictionary(data => data.GetPaymentMethodId(), data => data) ??
                       new Dictionary<PaymentMethodId, TransferProcessorData>();
            foreach (PaymentMethodId supportedPaymentMethod in factory.GetSupportedPaymentMethods())
            {
                conf.TryAdd(supportedPaymentMethod, null);
            }

            return new StoreTransferProcessorsView() { Factory = factory, Configured = conf };
        }).ToList());
    }
    
    [HttpPost("~/stores/{storeId}/transfer-processors/{id}/remove")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Remove(string storeId, string id)
    {
        var tcs = new TaskCompletionSource();
        _eventAggregator.Publish(new TransferProcessorUpdated()
        {
            Data = null,
            Id = id,
            Processed = tcs
        });
        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Message = "Transfer Processor removed"
        });
        await tcs.Task;
        return RedirectToAction("ConfigureStoreTransferProcessors",new {storeId});
        
    }

    public class StoreTransferProcessorsView
    {
        public Dictionary<PaymentMethodId, TransferProcessorData> Configured { get; set; }
        public ITransferProcessorFactory Factory { get; set; }
    }
}
