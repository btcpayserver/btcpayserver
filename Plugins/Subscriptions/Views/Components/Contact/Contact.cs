using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Views.UIStoreMembership.Components.Contact;

public class Contact : ViewComponent
{
    public IViewComponentResult Invoke(string type, string value)
        => View((type, value));
}
