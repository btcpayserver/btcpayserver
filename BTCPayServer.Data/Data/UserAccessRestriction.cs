using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data;

public static class UserAccessRestrictionConstants
{
    public const string Administrative = "Administrative";
    public const string Monetization = "Monetization";
}

[Table("user_access_restrictions")]
[PrimaryKey(nameof(ApplicationUserId), nameof(Type))]
public class UserAccessRestriction
{
    [Column("user_id")]
    public string ApplicationUserId { get; set; }

    [ForeignKey(nameof(ApplicationUserId))]
    public ApplicationUser ApplicationUser { get; set; }

    [Column("type")]
    public string Type { get; set; }

    [Column("created_at", TypeName = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; }

    public static void OnModelCreating(ModelBuilder builder)
    {
        var entity = builder.Entity<UserAccessRestriction>();
        entity.HasOne(restriction => restriction.ApplicationUser)
            .WithMany(user => user.AccessRestrictions)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
