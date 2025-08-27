using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data.Subscriptions;

[Table("subscriber_invoices")]
public class SubscriberInvoiceData
{
    [Column("invoice_id")]
    [Required]
    public string InvoiceId { get; set; }
    [Column("subscriber_id")]
    [Required]
    public long SubscriberId { get; set; }

    [Column("created_at", TypeName = "timestamptz")]
    [Required]
    public DateTimeOffset CreatedAt { get; set; }

    [ForeignKey(nameof(InvoiceId))]
    public InvoiceData Invoice { get; set; }

    [ForeignKey(nameof(SubscriberId))]
    public SubscriberData Subscriber { get; set; }

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<SubscriberInvoiceData>();
        b.HasKey(o => new { o.SubscriberId, o.InvoiceId });
        b.HasOne(o => o.Subscriber).WithMany().HasForeignKey(o => o.SubscriberId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(o => o.Invoice).WithMany().HasForeignKey(o => o.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        b.HasIndex(x => new { x.SubscriberId, x.CreatedAt });
    }
}
