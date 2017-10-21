using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.InvoicingModels
{
	// going with lowercase for property names to enable easy ToJson conversion
	// down the road I can look into mapper who transforms capital into lower case
	// because of different conventions between server and client side
	public class PaymentModel
	{
		public string serverUrl { get; set; }
		public string invoiceId { get; set; }
		public string btcAddress { get; set; }
		public string btcDue { get; set; }
		public string customerEmail { get; set; }
		public int expirationSeconds { get; set; }
		public string status { get; set; }
		public string merchantRefLink { get; set; }
		public int maxTimeSeconds { get; set; }

		// These properties are still not used in client side code
		// so will stick with C# notation for now
		public string StoreName { get; set; }
		public string ItemDesc { get; set; }
		public string TimeLeft { get; set; }
		public string Rate { get; set; }
		public string BTCAmount { get; set; }
		public string TxFees { get; set; }
		public string InvoiceBitcoinUrl { get; set; }
		public string BTCTotalDue { get; set; }
		public int TxCount { get; set; }
		public string BTCPaid { get; set; }
		public string StoreEmail { get; set; }

		public string OrderId
		{
			get; set;
		}
	}
}
