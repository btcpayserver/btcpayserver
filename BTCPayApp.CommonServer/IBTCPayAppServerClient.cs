using System.Threading;
using System.Threading.Tasks;

namespace BTCPayApp.CommonServer
{
    public interface IBTCPayAppServerClient
    {
        Task<string> ClientMethod1(string user, string message);
        Task ClientMethod2();
        Task<string> ClientMethod3(string user, string message, CancellationToken cancellationToken); // ca
    }
    
    public interface IBTCPayAppServerHub
    {
        Task<string> SendMessage(string user, string message);
        Task SomeHubMethod();
    }

}
