using System.Threading.Tasks;
using BTCPayServer.Client;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Abstractions.Contracts
{
    public interface IBTCPayServerClientFactory
    {
        Task<BTCPayServerClient> Create(string userId, params string[] storeIds);
        Task<BTCPayServerClient> Create(string userId, string[] storeIds, HttpContext httpRequest);
    }
}
