#nullable enable
namespace BTCPayServer.Rating;
public enum RateSource
{
    Coingecko,
    Direct
}
public record RateSourceInfo(string? Id, string DisplayName, string Url, RateSource Source = RateSource.Direct);
