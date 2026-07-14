using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Data;

public class EmailLogData
{
    public static EmailLogData Create(string? storeId) => new EmailLogData
        {
            Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16)),
            Timestamp = DateTimeOffset.UtcNow,
            StoreId = storeId
        };

    public string Id { get; set; }
    public string? StoreId { get; set; }
    public StoreData? Store { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string? Blob { get; set; }
    public bool Pruned { get; set; }

    internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<EmailLogData>();
        b.HasOne(o => o.Store).WithMany().OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(o => o.StoreId);
        b.HasIndex(o => o.Timestamp);
        b.Property(o => o.Blob).HasColumnType("JSONB");
    }
}

public static partial class ApplicationDbContextExtensions
{
    public static IQueryable<EmailLogData> GetStoreLogs(this IQueryable<EmailLogData> query, string storeId) => query.Where(o => o.StoreId == storeId).OrderByDescending(o => o.Timestamp);

    public static IQueryable<EmailLogData> GetServerLogs(this IQueryable<EmailLogData> query) => query.Where(o => o.StoreId == null).OrderByDescending(o => o.Timestamp);
}
