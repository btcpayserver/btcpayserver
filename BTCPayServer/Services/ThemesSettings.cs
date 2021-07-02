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

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [MaxLength(500)]
        [Display(Name = "Custom Bootstrap CSS file")]
        public string BootstrapCssUri { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [Display(Name = "Custom Creative Start CSS file")]
        public string CreativeStartCssUri { get; set; }
        public bool FirstRun { get; set; }
        public override string ToString()
        {
            // no logs
            return string.Empty;
        }
    }
}
