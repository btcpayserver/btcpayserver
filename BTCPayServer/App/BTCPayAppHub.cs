using System;
using System.Threading.Tasks;
using BTCPayApp.CommonServer;
using Microsoft.AspNetCore.SignalR;

namespace BTCPayServer.Controllers;

public class BTCPayAppHub : Hub<IBTCPayAppServerClient>, IBTCPayAppServerHub
{
    public async Task<string> SendMessage(string user, string message)
    {
        await Clients.All.ClientMethod1(user, message);
        return $"[Success] Call SendMessage : {user}, {message}";
    }

    public async Task SomeHubMethod()
    {
        await Clients.Caller.ClientMethod2();
    }
}

