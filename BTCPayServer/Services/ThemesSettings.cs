using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace BTCPayServer.Services
{
    public class ThemeSettings
    {
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [MaxLength(500)]
        [Display(Name = "Select Theme")]
        public string ThemeCssUri { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [MaxLength(500)]
        [Display(Name = "Custom Theme CSS file")]
        public string CustomThemeCssUri { get; set; }

        public string CssUri
        {
            get
            {
                return !string.IsNullOrWhiteSpace(CustomThemeCssUri)
                    ? CustomThemeCssUri
                    : ThemeCssUri;
            }
        }

        public bool FirstRun { get; set; }
        public override string ToString()
        {
            // no logs
            return string.Empty;
        }
    }
}
