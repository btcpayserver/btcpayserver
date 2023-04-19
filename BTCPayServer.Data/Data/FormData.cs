using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data;

public class FormData
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; }
    public string Name { get; set; }
    public string StoreId { get; set; }
    public StoreData Store { get; set; }
    public string Config { get; set; }
    public bool Public { get; set; }

    internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        builder.Entity<FormData>()
            .HasOne(o => o.Store)
            .WithMany(o => o.Forms).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<FormData>().HasIndex(o => o.StoreId);

        if (databaseFacade.IsNpgsql())
        {
            builder.Entity<FormData>()
                .Property(o => o.Config)
                .HasColumnType("JSONB");
        }
    }
}
