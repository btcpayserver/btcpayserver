using NBitcoin;
using NBXplorer;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Fees
{
	public class NBXplorerFeeProvider : IFeeProvider
	{
		public ExplorerClient ExplorerClient
		{
			get; set;
		}
		public FeeRate Fallback
		{
			get; set;
		}
		public int BlockTarget
		{
			get; set;
		}
		public async Task<FeeRate> GetFeeRateAsync()
		{
			try
			{
				return (await ExplorerClient.GetFeeRateAsync(BlockTarget).ConfigureAwait(false)).FeeRate;
			}
			catch(NBXplorerException ex) when(ex.Error.HttpCode == 400 && ex.Error.Code == "fee-estimation-unavailable")
			{
				return Fallback;
			}
		}
	}
}
