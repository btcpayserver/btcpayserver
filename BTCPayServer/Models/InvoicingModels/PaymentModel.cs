using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.InvoicingModels
{
    public class PaymentModel
    {
		public string InvoiceId
		{
			get; set;
		}

		public string OrderId
		{
			get; set;
		}
		public string BTCAddress
		{
			get; set;
		}

		public string BTCDue
		{
			get; set;
		}

		public string CustomerEmail
		{
			get; set;
		}

		public int ExpirationSeconds
		{
			get; set;
		}

		public int MaxTimeSeconds
		{
			get; set;
		}

		public string TimeLeft
		{
			get; set;
		}

		public string RedirectUrl
		{
			get; set;
		}


		public string StoreName
		{
			get; set;
		}

		public string ItemDesc
		{
			get; set;
		}

		public string Rate
		{
			get; set;
		}

		public string BTCAmount
		{
			get; set;
		}

		public string TxFees
		{
			get; set;
		}
		public string InvoiceBitcoinUrl
		{
			get;
			internal set;
		}
		public string BTCTotalDue
		{
			get;
			set;
		}
		public int TxCount
		{
			get; set;
		}
		public string BTCPaid
		{
			get; set;
		}
		public string StoreEmail
		{
			get; set;
		}
		public string Status
		{
			get;
			set;
		}
	}
}
