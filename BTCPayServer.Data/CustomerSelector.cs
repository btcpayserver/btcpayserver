namespace BTCPayServer.Data;

public abstract record CustomerSelector
{
    public static Id ById(string customerId) => new Id(customerId);
    public static ExternalRef ByExternalRef(string externalId) => new ExternalRef(externalId);
    public static Identity ByIdentity(string type, string value) => new Identity(type, value);
    public static Identity ByEmail(string email) => new Identity("Email", email);

    public record Id(string CustomerId) : CustomerSelector;
    public record ExternalRef(string Ref) : CustomerSelector;
    public record Identity(string Type, string Value) : CustomerSelector;
}
