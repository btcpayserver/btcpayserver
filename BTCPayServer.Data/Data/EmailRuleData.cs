#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data;

[Table("email_rules")]
public class EmailRuleData : BaseEntityData
{
    public static string GetWebhookTriggerName(string webhookType) => $"WH-{webhookType}";
    [Column("store_id")]
    public string? StoreId { get; set; }

    [Key]
    public long Id { get; set; }

    [ForeignKey(nameof(StoreId))]
    public StoreData? Store { get; set; }

    [Required]
    [Column("trigger")]
    public string Trigger { get; set; }  = null!;

    [Column("condition")]
    public string? Condition { get; set; }

    [Required]
    [Column("to")]
    public string[] To { get; set; }  = null!;

    [Required]
    [Column("subject")]
    public string Subject { get; set; } = null!;
    [Required]
    [Column("body")]
    public string Body { get; set; }  = null!;

    public class BTCPayAdditionalData
    {
        public bool CustomerEmail { get; set; }
    }
    public BTCPayAdditionalData? GetBTCPayAdditionalData() => this.GetAdditionalData<BTCPayAdditionalData>("btcpay");
    public void SetBTCPayAdditionalData(BTCPayAdditionalData? data) => this.SetAdditionalData("btcpay", data);

    internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<EmailRuleData>();
        BaseEntityData.OnModelCreateBase(b, builder, databaseFacade);
        b.Property(o => o.Id).UseIdentityAlwaysColumn();
        b.HasOne(o => o.Store).WithMany().OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(o => o.StoreId);
    }
}
public static partial class ApplicationDbContextExtensions
{
    public static IQueryable<EmailRuleData> GetRules(this IQueryable<EmailRuleData> query, string storeId)
    => query.Where(o => o.StoreId == storeId)
            .OrderBy(o => o.Id);

    public static Task<EmailRuleData[]> GetMatches(this DbSet<EmailRuleData> set, string? storeId, string trigger, JObject model)
    => set
        .FromSqlInterpolated($"""
                              SELECT * FROM email_rules
                              WHERE store_id IS NOT DISTINCT FROM {storeId} AND trigger = {trigger} AND (condition IS NULL OR jsonb_path_exists({model.ToString()}::JSONB, condition::JSONPATH))
                              """)
        .ToArrayAsync();

    public static Task<EmailRuleData?> GetRule(this IQueryable<EmailRuleData> query, string storeId, long id)
        => query.Where(o => o.StoreId == storeId && o.Id == id)
            .FirstOrDefaultAsync();
}
