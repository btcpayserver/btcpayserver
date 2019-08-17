using Microsoft.Extensions.Localization;

namespace BTCPayServer.Localization
{
    public class JsonLocalizationOptions : LocalizationOptions
    {
        public ResourcesType ResourcesType { get; set; } = ResourcesType.TypeBased;
    }
}
