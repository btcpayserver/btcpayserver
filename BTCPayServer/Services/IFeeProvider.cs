using NBitcoin;
using NBXplorer;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Services
{
	public interface IFeeProvider
	{
		Task<FeeRate> GetFeeRateAsync();
	}

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
			catch(NBXplorerException ex) when( ex.Error.HttpCode == 400 && ex.Error.Code == "fee-estimation-unavailable")
			{
				return Fallback;
			}
		}
	}

	public class FixedFeeProvider : IFeeProvider
	{
		public FixedFeeProvider(FeeRate feeRate)
		{
			FeeRate = feeRate;
		}

		public FeeRate FeeRate
		{
			get; set;
		}

		public Task<FeeRate> GetFeeRateAsync()
		{
			return Task.FromResult(FeeRate);
		}
	}
}
