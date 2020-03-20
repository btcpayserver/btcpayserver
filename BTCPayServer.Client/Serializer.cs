using System;
using System.Collections.Generic;
using System.Text;
using BTCPayServer.Client.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client
{
    public class Serializer
    {

        private static JsonSerializerSettings _GlobalSerializerSettings;
        public static JsonSerializerSettings GlobalSerializerSettings
        {
            get
            {
                if (_GlobalSerializerSettings is null)
                {
                    var serializer = new JsonSerializerSettings();
                    RegisterConverters(serializer);
                    _GlobalSerializerSettings = serializer;
                }
                return _GlobalSerializerSettings;
            }
        }
        public static void RegisterConverters(JsonSerializerSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            settings.Converters.Add(new PermissionJsonConverter());
        }
    }
}
