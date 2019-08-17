using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;

namespace BTCPayServer.Controllers
{
    public class LocaleController : Controller
    {
        [HttpPost]
        [Route("setlocale")]
        public IActionResult SetLocaleInCookie(string culture, string returnUrl)
        {
            CookieOptions option = new CookieOptions();
            option.Expires = DateTime.Now.AddYears(1);
            // TODO the path could be set better, dynamically. Not sure how to do that.
            option.Path = "/";

            Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName, "c=" + culture + "|uic=" + culture,
                option);

            return LocalRedirect(returnUrl);
        }
    }
}
