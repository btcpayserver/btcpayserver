using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Services.Stores;

public enum GenericFormOption
{
   [Display(Name = "Do not request any information")]
   None,

   [Display(Name = "Request email address only")]
   Email,

   [Display(Name = "Request shipping address")]
   Address
}

public static class CheckoutFormSelectList
{
    public static SelectList WithSelected(string selectedFormId)
    {
        var choices = new List<SelectListItem>
        {
            GenericOptionItem(GenericFormOption.None),
            GenericOptionItem(GenericFormOption.Email),
            GenericOptionItem(GenericFormOption.Address)
        };
        
        var chosen = choices.FirstOrDefault(t => t.Value == selectedFormId);
        return new SelectList(choices, nameof(SelectListItem.Value), nameof(SelectListItem.Text), chosen?.Value);
    }

    private static string DisplayName(GenericFormOption opt) => 
        typeof(GenericFormOption).DisplayName(opt.ToString());

    private static SelectListItem GenericOptionItem(GenericFormOption opt) =>
        new() { Text = DisplayName(opt), Value = opt == GenericFormOption.None ? null : opt.ToString() };
}
