using System.Threading.Tasks;
using BTCPayServer.Models;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components
{
    public class RobotsMetaViewComponent : ViewComponent
    {
        private readonly SettingsRepository _SettingsRepository;

        public RobotsMetaViewComponent(SettingsRepository settingsRepository)
        {
            _SettingsRepository = settingsRepository;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var policies = await _SettingsRepository.GetSettingAsync<PoliciesSettings>();

            return View(new RobotsMetaViewModel()
            {
                DiscourageSearchEngines = policies.DiscourageSearchEngines
            });
        }
    }


}
