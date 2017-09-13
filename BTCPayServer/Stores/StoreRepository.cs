using BTCPayServer.Data;
using BTCPayServer.Models;
using NBitcoin;
using NBitcoin.DataEncoders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Stores
{
	public class StoreRepository
	{
		private ApplicationDbContextFactory _ContextFactory;
		public StoreRepository(ApplicationDbContextFactory contextFactory)
		{
			_ContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
		}

		public async Task<StoreData> FindStore(string storeId)
		{
			using(var ctx = _ContextFactory.CreateContext())
			{
				return await ctx.FindAsync<StoreData>(storeId).ConfigureAwait(false);
			}
		}

		public async Task<StoreData> CreateStore(string userId)
		{
			using(var ctx = _ContextFactory.CreateContext())
			{
				StoreData store = new StoreData
				{
					Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(32))
				};
				var userStore = new UserStore
				{
					StoreDataId = store.Id,
					ApplicationUserId = userId
				};
				await ctx.AddAsync(store).ConfigureAwait(false);
				await ctx.AddAsync(userStore).ConfigureAwait(false);
				await ctx.SaveChangesAsync().ConfigureAwait(false);
				return store;
			}
		}

		public async Task<StoreData> GetStore(string userId)
		{
			using(var ctx = _ContextFactory.CreateContext())
			{
				return await ctx
					.Stores
					.Where(s => s.UserStores.Any(us => us.ApplicationUserId == userId))
					.FirstOrDefaultAsync().ConfigureAwait(false);
			}
		}

		public async Task UpdateStore(StoreData store)
		{
			using(var ctx = _ContextFactory.CreateContext())
			{
				var existing = await ctx.FindAsync<StoreData>(store.Id);
				ctx.Entry(existing).CurrentValues.SetValues(store);
				await ctx.SaveChangesAsync().ConfigureAwait(false);
			}
		}
	}
}
