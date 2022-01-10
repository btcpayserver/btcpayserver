using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using BTCPayServer.Data;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.AppViewModels
{
    public class CreateAppViewModel
    {
        public CreateAppViewModel()
        {
            SetApps();
        }
        class Format
        {
            public string Name { get; set; }
            public string Value { get; set; }
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

        void SetApps()
        {
            var defaultAppType = AppType.PointOfSale.ToString();
            var choices = typeof(AppType).GetEnumNames().Select(o => new Format
            {
                Name = typeof(AppType).DisplayName(o),
                Value = o
            }).ToArray();
            var chosen = choices.FirstOrDefault(f => f.Value == defaultAppType) ?? choices.FirstOrDefault();
            AppTypes = new SelectList(choices, nameof(chosen.Value), nameof(chosen.Name), chosen);
            SelectedAppType = chosen.Value;
        }

    }
}
