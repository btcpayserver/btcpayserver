using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BTCPayServer.Data
{
    public class AppData
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string StoreDataId
        {
            get; set;
        }
        public string AppType { get; set; }
        public StoreData StoreData
        {
            get; set;
        }
        public DateTimeOffset Created
        {
            get; set;
        }
        public string Settings { get; set; }

        public T GetSettings<T>() where T : class, new()
        {
            if (String.IsNullOrEmpty(Settings))
                return new T();
            return JsonConvert.DeserializeObject<T>(Settings);
        }

        public void SetSettings(object value)
        {
            Settings = value == null ? null : JsonConvert.SerializeObject(value);
        }
    }
}
