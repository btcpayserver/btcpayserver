namespace BTCPayServer.Contracts
{
    public interface ISyncSummaryProvider
    {
        bool AllAvailable();

        string Partial { get; }
    }
    
}
