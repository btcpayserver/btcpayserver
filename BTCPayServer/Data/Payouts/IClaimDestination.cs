namespace BTCPayServer.Data
{
    public interface IClaimDestination
    {
    }

    public interface IPayoutProof
    {
        string Link { get; }
        string Id { get; }
    }
}
