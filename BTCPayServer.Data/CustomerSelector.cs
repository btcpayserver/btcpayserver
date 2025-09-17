using System.Linq;

namespace BTCPayServer.Data;

public abstract record CustomerSelector
{
    public abstract IQueryable<CustomerData> Where(IQueryable<CustomerData> query);
    public static Id ById(string customerId) => new Id(customerId);
    public static ExternalRef ByExternalRef(string externalId) => new ExternalRef(externalId);
    public static Contact ByContact(string type, string value) => new Contact(type, value);
    public static Contact ByEmail(string email) => new Contact("Email", email);
    public record Id(string CustomerId) : CustomerSelector
    {
        public override IQueryable<CustomerData> Where(IQueryable<CustomerData> query)
        => query.Where(c => c.Id == CustomerId);
    }

    public record ExternalRef(string ExtenalRef) : CustomerSelector
    {
        public override IQueryable<CustomerData> Where(IQueryable<CustomerData> query)
        => query.Where(c => c.ExternalRef == ExtenalRef);
    }

    public record Contact(string Type, string Value) : CustomerSelector
    {
        public override IQueryable<CustomerData> Where(IQueryable<CustomerData> query)
            => query.Where(c => c.Contacts.Any(c => c.Type == Type && c.Value == Value));
    }
}
