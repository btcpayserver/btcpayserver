#nullable  enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data;

[Table("customers")]
public class CustomerData : BaseEntityData
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = null!;

    [Required]
    [Column("store_id")]
    public string StoreId { get; set; } = null!;

    [ForeignKey("StoreId")]
    public StoreData Store { get; set; } = null!;

    // Identity
    [Column("external_ref")]
    public string? ExternalRef { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    public List<CustomerIdentityData> CustomerIdentities { get; set; } = null!;

    public const string IdPrefix = "cust";
    public new static string GenerateId() => ValueGenerators.WithPrefix(IdPrefix)(null, null).Next(null!) as string ?? throw new InvalidOperationException("Bug, shouldn't happen");

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<CustomerData>();
        OnModelCreateBase(b, builder, databaseFacade);
        b.Property(x => x.Name).HasColumnName("name").HasColumnType("TEXT")
            .HasDefaultValueSql("''::TEXT");

        b.HasKey(x => new { x.Id });
        b.HasIndex(x => new { x.StoreId, x.ExternalRef }).IsUnique();
        b.Property(x => x.Id)
            .ValueGeneratedOnAdd()
            .HasValueGenerator(ValueGenerators.WithPrefix(IdPrefix));
    }

    public string? GetContact(string type)
        => (CustomerIdentities ?? throw ContactDataNotIncludedInEntity()).FirstOrDefault(c => c.Type == type)?.Value;

    private static InvalidOperationException ContactDataNotIncludedInEntity()
        => new InvalidOperationException("Bug: Contact data not included in entity. Use .Include(x => x.Contacts) to include it.");

    public class ContactSetter(CustomerData customer, string type)
    {
        public string Type { get; } = type;
        public void Set(string? value) => customer.SetContact(Type, value);
        public string? Get() => customer.GetContact(Type);
        public override string ToString() => $"{Get()} ({Type})";
    }

    [NotMapped]
    public ContactSetter Email => new ContactSetter(this, "Email");

    public void SetContact(string type, string? value)
    {
        if (CustomerIdentities is null)
            throw ContactDataNotIncludedInEntity();
        if (value is null)
        {
            CustomerIdentities.RemoveAll(c => c.Type == type);
            return;
        }

        var existing = CustomerIdentities.FirstOrDefault(c => c.Type == type);
        if (existing != null)
        {
            existing.Value = value;
        }
        else
        {
            CustomerIdentities.Add(new() { CustomerId = Id, Type = type, Value = value });
        }
    }

    public string? GetPrimaryIdentity() => Email.Get();
}
