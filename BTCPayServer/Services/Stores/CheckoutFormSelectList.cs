using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Forms;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Services.Stores;

public enum GenericFormOption
{
   [Display(Name = "Inherit from store settings")]
   InheritFromStore,

   [Display(Name = "Do not request any information")]
   None,

   [Display(Name = "Request email address only")]
   Email,

   [Display(Name = "Request shipping address")]
   Address
}

public static class CheckoutFormSelectList
{
    public static async Task<SelectList> ForStore(StoreData store, string selectedFormId, bool isStoreEntity, FormDataService formDataService)
    {
        var forms = await formDataService.GetForms(new FormDataService.FormQuery { Stores = new [] { store.Id }});
        var choices = new List<SelectListItem>();

        if (isStoreEntity)
        {
            var blob = store.GetStoreBlob();
            var inherit = GenericOptionItem(GenericFormOption.InheritFromStore);
            inherit.Text += Enum.TryParse<GenericFormOption>(blob.CheckoutFormId, out var item)
                ? $" ({DisplayName(item)})"
                : $" ({forms.Find(f => f.Id == blob.CheckoutFormId)?.Name ?? DisplayName(GenericFormOption.None) })";
            
            choices.Add(inherit);
        }
        
        choices.Add(new SelectListItem { Text = DisplayName(GenericFormOption.None), Value = null });
        choices.Add(GenericOptionItem(GenericFormOption.Email));
        choices.Add(GenericOptionItem(GenericFormOption.Address));
        forms.ForEach(data => choices.Add(new SelectListItem(data.Name, data.Id)));
        
        var chosen = choices.FirstOrDefault(t => t.Value == selectedFormId);
        return new SelectList(choices, nameof(SelectListItem.Value), nameof(SelectListItem.Text), chosen?.Value);
    }

    private static string DisplayName(GenericFormOption opt) => 
        typeof(GenericFormOption).DisplayName(opt.ToString());

    private static SelectListItem GenericOptionItem(GenericFormOption opt) =>
        new() { Text = DisplayName(opt), Value = opt.ToString() };
}
