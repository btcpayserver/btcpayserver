using System.Globalization;
using BTCPayServer.Lightning;
using Newtonsoft.Json;

namespace BTCPayServer.Client.JsonConverters
{
    public class LightMoneyJsonConverter : BTCPayServer.Lightning.JsonConverters.LightMoneyJsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value != null)
                writer.WriteValue(((LightMoney)value).MilliSatoshi.ToString(CultureInfo.InvariantCulture));
        }
    }
}
