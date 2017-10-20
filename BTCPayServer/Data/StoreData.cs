using BTCPayServer.Models;
using BTCPayServer.Services.Invoices;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Data
{
    public class StoreData
    {
		public string Id
		{
			get;
			set;
		}

		public List<UserStore> UserStores
		{
			get; set;
		}

		public string DerivationStrategy
		{
			get; set;
		}

		public string StoreName
		{
			get; set;
		}

		public SpeedPolicy SpeedPolicy
		{
			get; set;
		}

		public string StoreWebsite
		{
			get; set;
		}

		public byte[] StoreCertificate
		{
			get; set;
		}

		[NotMapped]
		public string Role
		{
			get; set;
		}
	}
}
