using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
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
            if (Blob is null && Blob2 is not null)
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
            if (jobj["expiryDate"].Type == JTokenType.Date)
            {
                jobj["expiryDate"] = new JValue(NBitcoin.Utils.DateTimeToUnixTime(jobj["expiryDate"].Value<DateTime>()));
                Blob2 = jobj.ToString(Newtonsoft.Json.Formatting.None);
            }
            return true;
        }
    }
}
