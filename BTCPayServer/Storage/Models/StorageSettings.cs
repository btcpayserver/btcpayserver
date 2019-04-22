using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Storage.Models
{
    public class StorageSettings
    {
        public StorageProvider Provider { get; set; }
        public string ConfigurationStr { get; set; }
        
        [NotMapped]
        public JObject Configuration
        {
            get => JsonConvert.DeserializeObject<JObject>(string.IsNullOrEmpty(ConfigurationStr) ? "{}" : ConfigurationStr);
            set => ConfigurationStr = value.ToString();
        }
    }
}
