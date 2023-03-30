using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BTCPayServer.Abstractions.Custodians;
using BTCPayServer.Abstractions.Form;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.CustodianAccountViewModels
{
    public class CreateCustodianAccountViewModel
    {

        public void SetCustodianRegistry(IEnumerable<ICustodian> custodianRegistry)
        {
            var choices = custodianRegistry.Select(o => new Format
            {
                Name = o.Name,
                Value = o.Code
            }).ToArray();
            var chosen = choices.FirstOrDefault();
            Custodians = new SelectList(choices, nameof(chosen.Value), nameof(chosen.Name), chosen);
        }

        class Format
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }

        [Required]
        [MaxLength(50)]
        [MinLength(1)]
        [Display(Name = "Name")]
        public string Name { get; set; }

        [Display(Name = "Store")]
        public string StoreId { get; set; }

        [Required]
        [Display(Name = "Custodian")]
        public string SelectedCustodian { get; set; }
        //
        public SelectList Custodians { get; set; }

        public Form ConfigForm { get; set; }
        public string Config { get; set; }
    }
}
