using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.StoreViewModels;

public class DateTimeSettingsViewModel
{
    [Display(Name = "Store timezone")]
    public string StoreTimeZone { get; set; }

    public string ServerTimeZone { get; set; }

    [Display(Name = "Date format")]
    public string PreferredDateFormat { get; set; }

    public DateFormatterOptions.Template SelectedTemplate => DateFormatterOptions.GetTemplate(PreferredDateFormat) ?? DateFormatterOptions.DateTemplates[0];
}
