using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public partial class PaymentRequestData : MigrationInterceptor.IHasMigration
    {
        [NotMapped]
        public bool Migrated { get; set; }

        public bool TryMigrate()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (Blob is (null or { Length: 0 }) && Blob2 is not null && Currency is not null && Title is not null)
                return false;
            if (Blob2 is null)
            {
                Blob2 = Blob is not (null or { Length: 0 }) ? MigrationExtensions.Unzip(Blob) : "{}";
                Blob2 = MigrationExtensions.SanitizeJSON(Blob2);
            }
            Blob = null;
#pragma warning restore CS0618 // Type or member is obsolete
            var jobj = JObject.Parse(Blob2);
            // Fixup some legacy payment requests
            if (jobj["expiryDate"]?.Type == JTokenType.Date)
            {
                var date = NBitcoin.Utils.UnixTimeToDateTime(NBitcoin.Utils.DateTimeToUnixTime(jobj["expiryDate"].Value<DateTime>()));
                jobj.Remove("expiryDate");
                Expiry = date;
            }
            else if (jobj["expiryDate"]?.Type == JTokenType.Integer)
            {
                var date = NBitcoin.Utils.UnixTimeToDateTime(jobj["expiryDate"].Value<long>());
                jobj.Remove("expiryDate");
                Expiry = date;
            }
			if (jobj["currency"] is not null)
			{
				Currency = jobj["currency"].Value<string>();
				jobj.Remove("currency");
			}
			if (jobj["amount"] is not null)
			{
				Amount = jobj["amount"] switch
				{
					JValue jv when jv.Type == JTokenType.Float => jv.Value<decimal>(),
					JValue jv when jv.Type == JTokenType.Integer => jv.Value<long>(),
					JValue jv when jv.Type == JTokenType.String && decimal.TryParse(jv.Value<string>(), CultureInfo.InvariantCulture, out var d) => d,
					_ => 0m
				};
				jobj.Remove("amount");
			}
			Blob2 = jobj.ToString(Newtonsoft.Json.Formatting.None);
            
            // Run Title migration separately (only if Title column exists)
            try
            {
                TryMigrateTitle();
            }
            catch
            {
                // Title column doesn't exist yet - will be migrated later
            }
            
            return true;
        }

        public bool TryMigrateTitle()
        {
            if (Blob2 is null || Title is not null)
                return false;
            
            var jobj = JObject.Parse(Blob2);
            Title = jobj["title"]?.Value<string>();
            if (Title is not null)
            {
                jobj.Remove("title");
                Blob2 = jobj.ToString(Newtonsoft.Json.Formatting.None);
                return true;
            }
            return false;
        }
    }
}
