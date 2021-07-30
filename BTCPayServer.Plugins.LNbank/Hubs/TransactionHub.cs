using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace BTCPayServer.Plugins.LNbank.Hubs
{
    public class TransactionHub : Hub
    {
        public override Task OnConnectedAsync()
        {
            return Task.CompletedTask;
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            return Task.CompletedTask;
        }
    }
}
