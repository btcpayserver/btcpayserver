using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.StoreViewModels;

public class DateTimeSettingsViewModel
{
    [Display(Name = "Store timezone")]
    public string StoreTimeZone { get; set; }

    public string ServerTimeZone { get; set; }

    [Display(Name = "Date format")]
    public string PreferredDateStyle { get; set; }

    [Display(Name = "Time format")]
    public string PreferredTimeStyle { get; set; }

    [Display(Name = "Use 12-hour time")]
    public bool PreferredHour12 { get; set; }
}
