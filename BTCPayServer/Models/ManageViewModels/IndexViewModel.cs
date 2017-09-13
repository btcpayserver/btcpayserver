using BTCPayServer.Invoicing;
using BTCPayServer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.ManageViewModels
{
    public class IndexViewModel
    {
        public string Username { get; set; }

        public bool IsEmailConfirmed { get; set; }

        [Required]
        [EmailAddress]
		[MaxLength(50)]
		public string Email { get; set; }

		[ExtPubKeyValidator]
		public string ExtPubKey { get; set; }

		[Display(Name = "Store Name")]
		[MaxLength(50)]
		public string StoreName
		{
			get; set;
		}

		[Display(Name = "Consider the invoice confirmed when the payment transaction...")]
		public SpeedPolicy SpeedPolicy
		{
			get; set;
		}

		[Phone]
        [Display(Name = "Phone number")]
		[MaxLength(50)]
		public string PhoneNumber { get; set; }

        public string StatusMessage { get; set; }

		[Url]
		[Display(Name = "Store Website")]
		public string StoreWebsite
		{
			get;
			set;
		}
	}
}
