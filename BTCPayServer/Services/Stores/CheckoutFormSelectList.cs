using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BTCPayServer.Data;
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
    public static SelectList ForStore(StoreData store, string selectedFormId, bool isStoreEntity)
    {
        var choices = new List<SelectListItem>();

        if (isStoreEntity)
        {
            var blob = store.GetStoreBlob();
            var inherit = GenericOptionItem(GenericFormOption.InheritFromStore);
            inherit.Text += Enum.TryParse<GenericFormOption>(blob.CheckoutFormId, out var item)
                ? $" ({DisplayName(item)})"
                : $" ({blob.CheckoutFormId})";
            
            choices.Add(inherit);
        }
        
        choices.Add(GenericOptionItem(GenericFormOption.None));
        choices.Add(GenericOptionItem(GenericFormOption.Email));
        choices.Add(GenericOptionItem(GenericFormOption.Address));
        
        var chosen = choices.FirstOrDefault(t => t.Value == selectedFormId);
        return new SelectList(choices, nameof(SelectListItem.Value), nameof(SelectListItem.Text), chosen?.Value);
    }

    private static string DisplayName(GenericFormOption opt) => 
        typeof(GenericFormOption).DisplayName(opt.ToString());

    private static SelectListItem GenericOptionItem(GenericFormOption opt) =>
        new() { Text = DisplayName(opt), Value = opt.ToString() };
}
