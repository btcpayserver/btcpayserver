using System;
using System.Collections.Generic;
using System.Text;
using BTCPayServer.Client.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client
{
    public class Serializer
    {
        public static void RegisterConverters(JsonSerializerSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            settings.Converters.Add(new PermissionJsonConverter());
        }
    }
}
