using BTCPayServer.Servcices.Invoices;
using BTCPayServer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.StoreViewModels
{
    public class StoreViewModel
    {
		[Display(Name = "Store Name")]
		[Required]
		[MaxLength(50)]
		[MinLength(1)]
		public string StoreName
		{
			get; set;
		}

		[Url]
		[Display(Name = "Store Website")]
		public string StoreWebsite
		{
			get;
			set;
		}

		[ExtPubKeyValidator]
		public string ExtPubKey
		{
			get; set;
		}

		[Display(Name = "Consider the invoice confirmed when the payment transaction...")]
		public SpeedPolicy SpeedPolicy
		{
			get; set;
		}

		public string StatusMessage
		{
			get; set;
		}
	}
}
