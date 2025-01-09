using System;
using System.Diagnostics.CodeAnalysis;
using BTCPayServer.Client.App;
using BTCPayServer.Lightning;
using Microsoft.AspNetCore.SignalR;
using NBitcoin;

namespace BTCPayServer.App;

public class BTCPayAppLightningConnectionStringHandler(
    IHubContext<BTCPayAppHub, IBTCPayAppHubClient> hubContext,
    BTCPayAppState appState)
    : ILightningConnectionStringHandler
{
    public ILightningClient Create(string connectionString, Network network, [UnscopedRef] out string error)
    {
        var kv = LightningConnectionStringHelper.ExtractValues(connectionString, out var type);
        if (type != "app")
        {
            error = null;
            return null;
        }
        if (!kv.TryGetValue("key", out var key))
        {
            error = "The key 'key' is mandatory for app connection strings";
            return null;
        }
        if (!kv.TryGetValue("user", out var user))
        {
            error = "The key 'user' is mandatory for app connection strings";
            return null;
        }
        try
        {
            var client =  new BTCPayAppLightningClient(hubContext, appState, key, user);
            error = null;
            return client;
        }
        catch (Exception e)
        {
            error = e.Message;
            return null;
        }
    }
}
