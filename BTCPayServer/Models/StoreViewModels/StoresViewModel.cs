using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.StoreViewModels
{
    public class StoresViewModel
    {
		public string StatusMessage
		{
			get; set;
		}
		public List<StoreViewModel> Stores
		{
			get; set;
		} = new List<StoreViewModel>();
		public class StoreViewModel
		{
			public string Name
			{
				get; set;
			}

			public string WebSite
			{
				get; set;
			}

			public string Id
			{
				get; set;
			}
			public Money Balance
			{
				get; set;
			}
		}
	}
}
