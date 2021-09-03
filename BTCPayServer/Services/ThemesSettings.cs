using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace BTCPayServer.Services
{
    public class ThemeSettings
    {
        [Display(Name = "Use custom theme")]
        public bool CustomTheme { get; set; }
        
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        [MaxLength(500)]
        [Display(Name = "Custom Theme CSS URL")]
        public string CustomThemeCssUri { get; set; }

        public string CssUri
        {
            get => CustomTheme ? CustomThemeCssUri : "/main/themes/default.css";
        }

        public bool FirstRun { get; set; }
        public override string ToString()
        {
            // no logs
            return string.Empty;
        }
    }
}
