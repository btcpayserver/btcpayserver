using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
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
        public string Name { get; set; }

        [Display(Name = "Store")]
        public string SelectedStore { get; set; }

        public void SetStores(StoreData[] stores)
        {
            var defaultStore = stores[0].Id;
            var choices = stores.Select(o => new Format() { Name = o.StoreName, Value = o.Id }).ToArray();
            var chosen = choices.FirstOrDefault(f => f.Value == defaultStore) ?? choices.FirstOrDefault();
            Stores = new SelectList(choices, nameof(chosen.Value), nameof(chosen.Name), chosen);
            SelectedStore = chosen.Value;
        }

        public SelectList Stores { get; set; }

        [Display(Name = "App type")]
        public string SelectedAppType { get; set; }

        public SelectList AppTypes { get; set; }

        void SetApps()
        {
            var defaultAppType = AppType.PointOfSale.ToString();
            var choices = typeof(AppType).GetEnumNames().Select(o => new Format() { Name = o, Value = o }).ToArray();
            var chosen = choices.FirstOrDefault(f => f.Value == defaultAppType) ?? choices.FirstOrDefault();
            AppTypes = new SelectList(choices, nameof(chosen.Value), nameof(chosen.Name), chosen);
            SelectedAppType = chosen.Value;
        }

    }
}
