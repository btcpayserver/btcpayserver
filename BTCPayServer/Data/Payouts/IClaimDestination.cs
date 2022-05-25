#nullable enable

namespace BTCPayServer.Data
{
    public interface IClaimDestination
    {
        public string? Id { get; }
        decimal? Amount { get; }
    }
}
