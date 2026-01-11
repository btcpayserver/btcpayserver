using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.ServerViewModels;
public class CreateDictionaryViewModel
{
    [Required(AllowEmptyStrings = false)]
    public string Name { get; set; }
    public string Fallback { get; set; }
    public SelectListItem[] DictionariesListItems { get; set; }

    internal CreateDictionaryViewModel SetDictionaries(LocalizerService.Dictionary[] dictionaries)
    {
        var items = dictionaries.Select(d => new SelectListItem(d.DictionaryName, d.DictionaryName, d.DictionaryName == Fallback)).ToArray();
        DictionariesListItems = items;
        return this;
    }
}
