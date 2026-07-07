#nullable enable
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Components.DateColumn;
public class DateColumn(IStringLocalizer stringLocalizer) : ViewComponent
{
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;

    public string? ColumnName { get; set; }
    public IViewComponentResult Invoke(string? columnName = null)
    {
        if (columnName is null)
            columnName = StringLocalizer["Date"];
        ColumnName = columnName;
        return View(this);
    }
}
