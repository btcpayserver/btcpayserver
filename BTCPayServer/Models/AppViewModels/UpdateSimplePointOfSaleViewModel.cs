using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.AppViewModels
{
    public class UpdateSimplePointOfSaleViewModel
    {
        public string StoreId { get; set; }
        [Required]
        [MaxLength(30)]
        public string Title { get; set; }
        [Required]
        [MaxLength(5)]
        public string Currency { get; set; }
        public string Example1 { get; internal set; }
        public string Example2 { get; internal set; }
        public string ExampleCallback { get; internal set; }
        public string InvoiceUrl { get; internal set; }

        [Display(Name = "Callback Notification Url")]
        [Uri]
        public string NotificationUrl { get; set; }

        [MaxLength(500)]
        [Display(Name = "Custom bootstrap CSS file")]
        public string CustomCSSLink { get; set; }

        public string Id { get; set; }

        [Display(Name = "Redirect invoice to redirect url automatically after paid")]
        public string RedirectAutomatically { get; set; } = string.Empty;

        public string AppId { get; set; }
        public string SearchTerm { get; set; }

        public SelectList RedirectAutomaticallySelectList =>
            new SelectList(new List<SelectListItem>()
            {
                new SelectListItem()
                {
                    Text = "Yes",
                    Value = "true"
                },
                new SelectListItem()
                {
                    Text = "No",
                    Value = "false"
                },
                new SelectListItem()
                {
                    Text = "Use Store Settings",
                    Value = ""
                }
            }, nameof(SelectListItem.Value), nameof(SelectListItem.Text), RedirectAutomatically);

        public string EmbeddedCSS { get; set; }
    }
}
