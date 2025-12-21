namespace BTCPayServer.Services
{
    public interface IFeeProviderFactory
    {
        IFeeProvider CreateFeeProvider(BTCPayNetworkBase network);
    }
}
