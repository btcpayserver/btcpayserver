using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BTCPayServer.Data.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data;

[Table("customers_contacts")]
public class CustomerContactData
{
    [Column("customer_id")]
    public string CustomerId { get; set; }

    [ForeignKey(nameof(CustomerId))]
    public CustomerData Customer { get; set; }

    [Required]
    [Column("type")]
    public string Type { get; set; }
    [Required]
    [Column("value")]
    public string Value { get; set; }

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<CustomerContactData>();
        b.HasKey(x=> new { x.CustomerId, x.Type });
        b.HasOne(x => x.Customer).WithMany(x => x.Contacts).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
    }
}
