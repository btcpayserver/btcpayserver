using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.InvoicingModels
{
	public class CreateInvoiceModel
	{
		[Required]
		public double? Amount
		{
			get; set;
		}

		public string OrderId
		{
			get; set;
		}

		public string ItemDesc
		{
			get; set;
		}

		public string PosData
		{
			get; set;
		}

		[EmailAddress]
		public string BuyerEmail
		{
			get; set;
		}
	}
}
