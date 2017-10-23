using BTCPayServer.Models;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using NBXplorer;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
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
		public byte[] StoreBlob
		{
			get;
			set;
		}

		public StoreBlob GetStoreBlob(Network network)
		{
			return StoreBlob == null ? new StoreBlob() : new Serializer(network).ToObject<StoreBlob>(Encoding.UTF8.GetString(StoreBlob));
		}

		public void SetStoreBlob(StoreBlob storeBlob, Network network)
		{
			StoreBlob = Encoding.UTF8.GetBytes(new Serializer(network).ToString(storeBlob));
		}
	}

	public class StoreBlob
	{
		public bool NetworkFeeDisabled
		{
			get; set;
		}
	}
}
