using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class Language
    {
        public Language(string code, string displayName)
        {
            DisplayName = displayName;
            Code = code;
        }

        [JsonProperty("code")]
        public string Code { get; set; }
        [JsonProperty("currentLanguage")]
        public string DisplayName { get; set; }
    }
}
