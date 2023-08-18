using System.Threading;
using System.Threading.Tasks;

namespace BTCPayApp.CommonServer
{
    public interface IBTCPayAppServerClient
    {
        Task OnTransactionDetected(string txid);
        Task NewBlock(string block);
    }
    
    public interface IBTCPayAppServerHub
    {
        Task Handshake(AppHandshake handshake);

    }

    public class AppHandshake
    {
        public string DerivationScheme { get; set; }
    }
    
    
    
    

}
