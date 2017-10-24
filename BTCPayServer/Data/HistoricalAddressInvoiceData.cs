using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Data
{
    public class HistoricalAddressInvoiceData
    {
		public string InvoiceDataId
		{
			get; set;
		}

		public string Address
		{
			get; set;
		}

		public DateTimeOffset Assigned
		{
			get; set;
		}

		public DateTimeOffset? UnAssigned
		{
			get; set;
		}
	}
}
