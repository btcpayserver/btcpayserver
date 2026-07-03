using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Components.SearchStringInput;

[ViewComponent]
public class SearchStringInput(IStringLocalizer stringLocalizer) : ViewComponent
{
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;

    public IViewComponentResult Invoke(SearchString searchString, string placeholder = null)
    => View(new Model { SearchString = searchString, Placeholder = placeholder ?? StringLocalizer["Search…"] });

    public class Model
    {
        public SearchString SearchString { get; init; }
        public string Placeholder { get; init; }
    }
}
