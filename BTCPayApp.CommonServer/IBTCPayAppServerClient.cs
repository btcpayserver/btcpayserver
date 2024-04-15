using System.Threading.Tasks;

namespace BTCPayApp.CommonServer;

public interface IBTCPayAppServerClient
{
    Task TransactionDetected(string txid);
    Task NewBlock(string block);
}
    
public interface IBTCPayAppServerHub
{
    Task Handshake(AppHandshake handshake);
    Task GetTransactions();
}

public class AppHandshake
{
    public string? DerivationScheme { get; set; }
}
