using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Services.Apps;
using BTCPayServer.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.AppViewModels
{
    public class UpdatePointOfSaleViewModel
    {
        public string StoreId { get; set; }
        public string StoreName { get; set; }

        [Required]
        [MaxLength(30)]
        public string Title { get; set; }
        [Required]
        [MaxLength(5)]
        public string Currency { get; set; }
        public string Template { get; set; }

        [Display(Name = "Default view")]
        public PosViewType DefaultView { get; set; }
        [Display(Name = "User can input custom amount")]
        public bool ShowCustomAmount { get; set; }
        [Display(Name = "User can input discount in %")]
        public bool ShowDiscount { get; set; }
        [Display(Name = "Enable tips")]
        public bool EnableTips { get; set; }
        public string Example1 { get; internal set; }
        public string Example2 { get; internal set; }
        public string ExampleCallback { get; internal set; }
        public string InvoiceUrl { get; internal set; }

        [Display(Name = "Callback Notification Url")]
        [Uri]
        public string NotificationUrl { get; set; }
        [Display(Name = "Redirect Url")]
        [Uri]
        public string RedirectUrl { get; set; }

        [Required]
        [MaxLength(30)]
        [Display(Name = "Text to display on each buttons for items with a specific price")]
        public string ButtonText { get; set; }
        [Required]
        [MaxLength(30)]
        [Display(Name = "Text to display on buttons next to the input allowing the user to enter a custom amount")]
        public string CustomButtonText { get; set; }
        [Required]
        [MaxLength(30)]
        [Display(Name = "Text to display in the tip input")]
        public string CustomTipText { get; set; }
        [MaxLength(30)]
        [Display(Name = "Tip percentage amounts (comma separated)")]
        public string CustomTipPercentages { get; set; }

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
        public string Description { get; set; }
    }
}
