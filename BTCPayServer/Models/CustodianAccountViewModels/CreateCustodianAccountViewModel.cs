using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BTCPayServer.Abstractions.Custodians;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.CustodianAccountViewModels
{
    public class CreateCustodianAccountViewModel
    {
        private readonly IEnumerable<ICustodian> _custodianRegistry;
        
        public CreateCustodianAccountViewModel(
            string storeId,
            IEnumerable<ICustodian> custodianRegistry)
        {
            this.StoreId = storeId;
            _custodianRegistry = custodianRegistry;
            var choices = _custodianRegistry.Select(o => new Format
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
        
        // TODO Allow the user to name each custodian account (i.e. he has 2 Kraken accounts and needs to be able to differentiate them)
        // [Required]
        // [MaxLength(50)]
        // [MinLength(1)]
        // [Display(Name = "Custodian Account Name")]
        // public string AppName { get; set; }

        [Display(Name = "Store")]
        public string StoreId { get; set; }

        [Display(Name = "Custodian")]
        public string SelectedCustodian { get; set; }
        //
        public SelectList Custodians { get; set; }

    }
}
