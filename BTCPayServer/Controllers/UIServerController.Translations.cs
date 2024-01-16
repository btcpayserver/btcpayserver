using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class UIServerController
    {
        [HttpGet("server/translations")]
        public IActionResult ServerTranslations()
        {
            return View(new ServerTranslationsViewModel().SetTranslations(_localizer.Translations));
        }
        [HttpPost("server/translations")]
        public async Task<IActionResult> ServerTranslations(ServerTranslationsViewModel viewModel)
        {
            var translation = Translations.CreateFromText(viewModel.Translations);
            await _localizer.Save(translation);
            TempData[WellKnownTempData.SuccessMessage] = "Translations updated";
            return RedirectToAction();
        }
    }
}
