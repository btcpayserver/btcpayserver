#nullable enable
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using BTCPayServer.Data.Subscriptions;

namespace BTCPayServer.Data;

public abstract record CustomerSelector
{
    public static readonly Regex ReferenceRegex = new(@"^\[(.*)\]$");
    public static bool TryParse(string str, [MaybeNullWhen(false)] out CustomerSelector selector)
    {
        ArgumentNullException.ThrowIfNull(str);
        selector = null;
        if (str.StartsWith(CustomerData.IdPrefix + "_") && !str.Contains("@", StringComparison.OrdinalIgnoreCase))
        {
            selector = ById(str);
            return true;
        }
        else if (ReferenceRegex.Match(str) is { Success: true } match)
        {
            selector = ByExternalRef(match.Groups[1].Value);
            return true;
        }
        else if (str.Split(':', 2, StringSplitOptions.RemoveEmptyEntries) is { Length: 2 } arr)
        {
            selector = ByIdentity(arr[0], arr[1]);
            return true;
        }
        else if (str.Contains('@', StringComparison.InvariantCulture))
        {
            selector = ByEmail(str);
            return true;
        }
        return false;
    }

    public static Id ById(string customerId) => new Id(customerId);
    public static ExternalRef ByExternalRef(string externalId) => new ExternalRef(externalId);
    public static Identity ByIdentity(string type, string value) => new Identity(type, value);
    public static Identity ByEmail(string email) => new Identity("Email", email);

    public record Id(string CustomerId) : CustomerSelector
    {
        public override string ToString() => CustomerId;
    }

    public record ExternalRef(string Ref) : CustomerSelector
    {
        public override string ToString() => $"[{Ref}]";
    }
    public record Identity(string Type, string Value) : CustomerSelector
    {
        public override string ToString() => $"{Type}:{Value}";
    }
}
