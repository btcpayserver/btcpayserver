using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BTCPayApp.CommonServer;
using BTCPayServer.Controllers;
using BTCPayServer.Lightning;
using Microsoft.AspNetCore.SignalR;
using NBitcoin;

namespace BTCPayServer.App;

public class BTCPayAppLightningConnectionStringHandler:ILightningConnectionStringHandler
{
    private readonly IHubContext<BTCPayAppHub, IBTCPayAppHubClient> _hubContext;
    private readonly BTCPayAppState _appState;
    private readonly DefaultHubLifetimeManager<BTCPayAppHub> _lifetimeManager;

    public BTCPayAppLightningConnectionStringHandler(IHubContext<BTCPayAppHub, IBTCPayAppHubClient> hubContext, BTCPayAppState appState)
    {
        _hubContext = hubContext;
        _appState = appState;
    }
    
    public ILightningClient Create(string connectionString, Network network, [UnscopedRef] out string error)
    {
        var kv = LightningConnectionStringHelper.ExtractValues(connectionString, out var type);
        if (type != "app")
        {
            error = null;
            return null;
        }
        
        
        if (!kv.TryGetValue("group", out var key))
        {
            error = $"The key 'group' is mandatory for app connection strings";
            
            return null;
        }

        if (!_appState.NodeToConnectionId.TryGetValue(key, out var connectionId) || !_appState.GroupToConnectionId.TryGetValues(key, out var conns) || !conns.Contains(connectionId))
        {
            error = $"The group {key} is not connected";
            return null;
        }
        error = null;
        return new BTCPayAppLightningClient(_hubContext, _appState, key );
    }
    
    
}
