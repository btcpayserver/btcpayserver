using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Client.Models;
using BTCPayServer.Validation;
using static BTCPayServer.Data.StoreBlob;

namespace BTCPayServer.Models.StoreViewModels
{
    public class StoreViewModel
    {
        public List<StoreDerivationScheme> DerivationSchemes { get; set; }
        public List<StoreLightningNode> LightningNodes { get; set; }
        public bool HintWallet { get; set; }
        public bool HintLightning { get; set; }
        public bool CanDelete { get; set; }
        
        [Display(Name = "Store ID")]
        public string Id { get; set; }
        
        [Display(Name = "Store Name")]
        [Required]
        [MaxLength(50)]
        [MinLength(1)]
        public string StoreName { get; set; }

        [Uri]
        [Display(Name = "Store Website")]
        [MaxLength(500)]
        public string StoreWebsite { get; set; }
    }
}
