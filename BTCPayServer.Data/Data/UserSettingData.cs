using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data;

public class UserSettingData
{
    public string Name { get; set; }
    public string UserId { get; set; }

    public string Value { get; set; }

    public ApplicationUser User { get; set; }

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        builder.Entity<UserSettingData>().HasKey(data => new { data.UserId, data.Name });
        builder.Entity<UserSettingData>()
            .HasOne(o => o.User)
            .WithMany().OnDelete(DeleteBehavior.Cascade);

        builder.Entity<UserSettingData>()
            .Property(o => o.Value)
            .HasColumnType("JSONB");
    }
}
