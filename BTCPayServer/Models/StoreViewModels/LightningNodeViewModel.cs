using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BTCPayServer.Models.StoreViewModels
{
    public enum LightningNodeType
    {
        Internal,
        Custom
    }

    public class LightningNodeViewModel
    {
        public LightningNodeType LightningNodeType { get; set; }
        // The authorised store is set by the controller; a form-supplied id would reach view extensions on re-render.
        [BindNever]
        public string StoreId { get; set; }
        public string CryptoCode { get; set; }
        public bool CanUseInternalNode { get; set; }
        public bool SkipPortTest { get; set; }

        [Display(Name = "Enabled")]
        public bool Enabled { get; set; } = true;

        [Display(Name = "Connection string")]
        public string ConnectionString { get; set; }
    }
}
