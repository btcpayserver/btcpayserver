#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data;

[Table("subscription_plans")]
public class SubscriptionPlanData : BaseEntityData
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = null!;

    [Required]
    [Column("store_id")]
    public string StoreId { get; set; } = null!;

    [ForeignKey("StoreId")]
    public StoreData Store { get; set; } = null!;

    [Required]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Column("status")]
    public PlanStatus Status { get; set; } = PlanStatus.Draft;

    [Required]
    [Column("price")]
    public decimal Price { get; set; }

    [Required]
    [Column("currency")]
    public string Currency { get; set; } = string.Empty;

    [Required]
    [Column("recurring_type")]
    public RecurringInterval RecurringType { get; set; } = RecurringInterval.Monthly;

    [Required]
    [Column("grace_period_days")]
    public int GracePeriodDays { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("allow_upgrade")]
    public bool AllowUpgrade { get; set; }

    [Column("members_count")]
    public int MemberCount { get; set; }


    public class BTCPayAdditionalData
    {
        public List<SubscriptionPlanItem>? PlanItems { get; set; }
        [JsonExtensionData]
        public Dictionary<string, JToken>? AdditionalData { get; set; }

        public bool HasDuplicateIds(ModelStateDictionary modelState, string property, string errorMessage)
        {
            if (PlanItems is null)
                return false;
            HashSet<string> ids = new();
            bool dups = false;
            for (int i = 0; i < PlanItems.Count; i++)
            {
                if (!ids.Add(PlanItems[i].Id))
                {
                    modelState.AddModelError(string.Format(property, i), errorMessage);
                    dups = true;
                }
            }

            return dups;
        }
    }

    public static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        var b = builder.Entity<SubscriptionPlanData>();
        b.Property(x => x.Status).HasConversion<string>();
        b.Property(x => x.RecurringType).HasConversion<string>();
    }

    public class SubscriptionPlanItem
    {
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string? ShortDescription { get; set; }
    }

    public enum PlanStatus
    {
        Active,
        Inactive,
        Draft
    }

    public enum RecurringInterval
    {
        Monthly,
        Quarterly,
        Yearly,
        OneTime
    }
}
