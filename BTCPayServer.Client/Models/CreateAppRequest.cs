using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models
{
    class DateTimeJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(DateTime);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.Value == null)
            {
                return null;
            }

            if (reader.Value is not long)
            {
                throw new JsonSerializationException($"Cannot convert {reader.Value} to a datetime value. Please provide a valid UNIX timestamp");
            }

            var v = (long)reader.Value;
            Check(v);

            try 
            {
                return unixRef + TimeSpan.FromSeconds(v);
            }
            catch (Exception ex)
            {
                if (ex is System.ArgumentOutOfRangeException)
                {
                    // It's possible someone might pass the timestamp in milliseconds so let's just handle it
                    return unixRef + TimeSpan.FromMilliseconds(v / 1000);
                }
                else
                {
                    throw new JsonSerializationException($"Cannot convert {reader.Value} to a datetime value.");
                }
            }

        }

        static readonly DateTime unixRef = new DateTime(1970, 1, 1, 0, 0, 0);
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var date = ((DateTime)value).ToUniversalTime();
            long v = (long)(date - unixRef).TotalMilliseconds;
            Check(v);
            writer.WriteValue(v);
        }

        private static void Check(long v)
        {
            if (v < 0)
                throw new JsonSerializationException("Invalid datetime (less than 1/1/1970)");
        }
    }

    public enum PosViewType
    {
        Static,
        Cart,
        Light,
        Print
    }

    public class CreateAppRequest
    {
        public string AppName { get; set; }
        public string AppType { get; set; }
    }

    public class CreatePointOfSaleAppRequest : CreateAppRequest
    {
        public string Currency { get; set; } = null;
        public string Title { get; set; } = null;
        public string Description { get; set; } = null;
        public string Template { get; set; } = null;
        [JsonConverter(typeof(StringEnumConverter))]
        public PosViewType DefaultView { get; set; }
        public bool ShowCustomAmount { get; set; } = false;
        public bool ShowDiscount { get; set; } = true;
        public bool EnableTips { get; set; } = true;
        public string CustomAmountPayButtonText { get; set; } = null;
        public string FixedAmountPayButtonText { get; set; } = null;
        public string TipText { get; set; } = null;
        public string CustomCSSLink { get; set; } = null;
        public string NotificationUrl { get; set; } = null;
        public string RedirectUrl { get; set; } = null;
        public bool? RedirectAutomatically { get; set; } = null;
        public bool? RequiresRefundEmail { get; set; } = null;
        public string CheckoutFormId { get; set; } = null;
        public string EmbeddedCSS { get; set; } = null;
        public CheckoutType? CheckoutType { get; set; } = null;
    }

    public enum CrowdfundResetEvery
    {
        Never,
        Hour,
        Day,
        Month,
        Year
    }

    public class CreateCrowdfundAppRequest : CreateAppRequest
    {
        public string Title { get; set; } = null;
        public bool? Enabled { get; set; } = null;
        public bool? EnforceTargetAmount { get; set; } = null;
        [JsonConverter(typeof(DateTimeJsonConverter))]
        public DateTime? StartDate { get; set; } = null;
        public string TargetCurrency { get; set; } = null;
        public string Description { get; set; } = null;
        [JsonConverter(typeof(DateTimeJsonConverter))]
        public DateTime? EndDate { get; set; } = null;
        public decimal? TargetAmount { get; set; } = null;
        public string CustomCSSLink { get; set; } = null;
        public string MainImageUrl { get; set; } = null;
        public string EmbeddedCSS { get; set; } = null;
        public string NotificationUrl { get; set; } = null;
        public string Tagline { get; set; } = null;
        public string PerksTemplate { get; set; } = null;
        public bool? SoundsEnabled { get; set; } = null;
        public string DisqusShortname { get; set; } = null;
        public bool? AnimationsEnabled { get; set; } = null;
        public int? ResetEveryAmount { get; set; } = null;
        [JsonConverter(typeof(StringEnumConverter))]
        public CrowdfundResetEvery ResetEvery { get; set; } = CrowdfundResetEvery.Never;
        public bool? DisplayPerksValue { get; set; } = null;
        public bool? DisplayPerksRanking { get; set; } = null;
        public bool? SortPerksByPopularity { get; set; } = null;
        public string Sounds { get; set; } = null;
        public string AnimationColors { get; set; } = null;
    }
}
