using System.ComponentModel.DataAnnotations;
using System.Linq;
using BTCPayServer.Plugins.PointOfSale;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.AppViewModels
{
    public class CreateAppViewModel
    {
        public CreateAppViewModel()
        {
        }

        public CreateAppViewModel(AppService appService)
        {
            SetApps(appService);
        }

        [Required]
        [MaxLength(50)]
        [MinLength(1)]
        [Display(Name = "App Name")]
        public string AppName { get; set; }

        [Display(Name = "Store")]
        public string StoreId { get; set; }

        [Display(Name = "App Type")]
        public string SelectedAppType { get; set; }

        public SelectList AppTypes { get; set; }
        public string AppType { get; set; }

        private void SetApps(AppService appService)
        {
            var defaultAppType = PointOfSaleAppType.AppType;
            var choices = appService.GetAvailableAppTypes().Select(pair =>
                new SelectListItem(pair.Value, pair.Key, pair.Key == defaultAppType));

            var chosen = choices.FirstOrDefault(f => f.Value == defaultAppType) ?? choices.FirstOrDefault();
            AppTypes = new SelectList(choices, nameof(chosen.Value), nameof(chosen.Text), chosen);
            SelectedAppType = chosen.Value;
        }

    }
}
