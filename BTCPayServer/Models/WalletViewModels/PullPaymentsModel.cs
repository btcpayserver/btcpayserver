using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.WalletViewModels
{
    public class PullPaymentsModel: BasePagingViewModel
    {
        public class PullPaymentModel
        {
            public class ProgressModel
            {
                public int CompletedPercent { get; set; }
                public int AwaitingPercent { get; set; }
                public string Completed { get; set; }
                public string Awaiting { get; set; }
                public string Limit { get; set; }
                public string ResetIn { get; set; }
                public string EndIn { get; set; }
            }
            public string Id { get; set; }
            public string Name { get; set; }
            public string ProgressText { get; set; }
            public ProgressModel Progress { get; set; }
            public DateTimeOffset StartDate { get; set; }
            public DateTimeOffset? EndDate { get; set; }
        }

        public List<PullPaymentModel> PullPayments { get; set; } = new List<PullPaymentModel>();
    }

    public class NewPullPaymentModel
    {
        [MaxLength(30)]
        public string Name { get; set; }
        [Required]
        public decimal Amount
        {
            get; set;
        }
        [Required]
        [ReadOnly(true)]
        public string Currency { get; set; }
        [MaxLength(500)]
        [Display(Name = "Custom CSS URL")]
        public string CustomCSSLink { get; set; }
        [Display(Name = "Custom CSS Code")]
        public string EmbeddedCSS { get; set; }

        public IEnumerable<string> PaymentMethods { get; set; }
        public IEnumerable<SelectListItem> PaymentMethodItems { get; set; }
    }
}
