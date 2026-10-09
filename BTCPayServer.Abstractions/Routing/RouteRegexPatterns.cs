namespace BTCPayServer.Abstractions.Routing;

public static class RouteRegexPatterns
{
    /// <summary>The Bitcoin Base58 character class, without a quantifier or anchors.</summary>
    public const string Base58Characters = "[1-9A-HJ-NP-Za-km-z]";

    public const string Base58 = $"^{Base58Characters}+\\z";
    public const string GuidD = "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\\z";
    public const string Hex64 = "^[0-9a-fA-F]{64}\\z";
}
