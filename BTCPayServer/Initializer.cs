using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Logging;
using BTCPayServer.Events;

namespace BTCPayServer
{
    public class Initializer
    {
        EventAggregator _Aggregator;
        CallbackController _CallbackController;
        public Initializer(EventAggregator aggregator, 
            CallbackController callbackController
            )
        {
            _Aggregator = aggregator;
            _CallbackController = callbackController;
        }
        public void Init()
        {
            _Aggregator.Subscribe<NBXplorerStateChangedEvent>(async (s, evt) =>
            {
                if (evt.NewState == NBXplorerState.Ready)
                {
                    s.Unsubscribe();
                    try
                    {
                        var callback = await _CallbackController.GetCallbackBlockUriAsync();
                        await _CallbackController.RegisterCallbackBlockUriAsync(callback);
                        Logs.PayServer.LogInformation($"Registering block callback to " + callback);
                    }
                    catch (Exception ex)
                    {
                        Logs.PayServer.LogError(ex, "Could not register block callback");
                        s.Resubscribe();
                    }
                }
            });
        }
    }
}
