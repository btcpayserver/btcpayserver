namespace BTCPayServer.Abstractions.Contracts
{
    public interface ISyncSummaryProvider
    {
        bool AllAvailable();

        string Partial { get; }
    }
}
