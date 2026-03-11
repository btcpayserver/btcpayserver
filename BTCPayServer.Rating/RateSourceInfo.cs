#nullable enable
using System;

namespace BTCPayServer.Rating;
public enum RateSource
{
    Direct
}
public record RateSourceInfo(string? Id, string DisplayName, string Url, RateSource Source = RateSource.Direct)
{
    public string? Id { get; init; } = ValidateId(Id);

    private static string? ValidateId(string? id)
    {
        if (id is null)
            return null;

        if (!Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(id))
            throw new FormatException($"Id '{id}' must be a valid C# identifier");

        return id;
    }
}
