using BTCPayApp.CommonServer;
using Microsoft.AspNetCore.SignalR;

namespace BTCPayServer.Controllers;

public class BTCPayAppHub : Hub<IBTCPayAppServerClient>
{
}