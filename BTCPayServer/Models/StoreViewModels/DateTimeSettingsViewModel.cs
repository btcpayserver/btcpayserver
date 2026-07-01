using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.StoreViewModels;

public class DateTimeSettingsViewModel
{
    [Display(Name = "Store timezone")]
    public string StoreTimeZone { get; set; }

    public string ServerTimeZone { get; set; }

    [Display(Name = "Date format")]
    public string PreferredDateFormat { get; set; }

    [Display(Name = "Time format")]
    public string PreferredTimeFormat { get; set; }

    public DateFormatterOptions.Template SelectedTemplate => DateFormatterOptions.GetTemplate(PreferredDateFormat) ?? DateFormatterOptions.DateTemplates[0];

    public DateFormatterOptions.Template SelectedTimeTemplate => DateFormatterOptions.GetTimeTemplate(PreferredTimeFormat) ?? DateFormatterOptions.TimeTemplates[0];
}
