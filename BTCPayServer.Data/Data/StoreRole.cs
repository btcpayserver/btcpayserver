
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Newtonsoft.Json;

namespace BTCPayServer.Data;

public class StoreRole
{
    public string Id { get; set; }
    public string StoreDataId { get; set; }
    public string Role { get; set; }
    public List<string> Permissions { get; set; }
    public List<UserStore> Users { get; set; }
    public StoreData StoreData { get; set; }

    internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        builder.Entity<StoreRole>(entity =>
        {
            entity.HasOne(e => e.StoreData)
                .WithMany(s => s.StoreRoles)
                .HasForeignKey(e => e.StoreDataId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(false);
            
            entity.HasIndex(entity => new {entity.StoreDataId, entity.Role}).IsUnique();
        });
    }
}
