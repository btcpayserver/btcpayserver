using BTCPayServer.Data;
using BTCPayServer.Models;
using NBitcoin;
using NBitcoin.DataEncoders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Services.Stores
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

		public async Task<StoreData> FindStore(string storeId, string userId)
		{
			if(userId == null)
				throw new ArgumentNullException(nameof(userId));
			using(var ctx = _ContextFactory.CreateContext())
			{
				return (await ctx
					.UserStore
					.Where(us => us.ApplicationUserId == userId && us.StoreDataId == storeId)
					.Select(us => new
					{
						Store = us.StoreData,
						Role = us.Role
					}).ToArrayAsync())
					.Select(us =>
					{
						us.Store.Role = us.Role;
						return us.Store;
					}).FirstOrDefault();
			}
		}

		public async Task<StoreData[]> GetStoresByUserId(string userId)
		{
			using(var ctx = _ContextFactory.CreateContext())
			{
				return await ctx.UserStore
					.Where(u => u.ApplicationUserId == userId)
					.Select(u => u.StoreData)
					.ToArrayAsync();
			}
		}

		public async Task<StoreData> CreateStore(string ownerId, string name)
		{
			if(string.IsNullOrEmpty(name))
				throw new ArgumentException("name should not be empty", nameof(name));
			using(var ctx = _ContextFactory.CreateContext())
			{
				StoreData store = new StoreData
				{
					Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(32)),
					StoreName = name
				};
				var userStore = new UserStore
				{
					StoreDataId = store.Id,
					ApplicationUserId = ownerId,
					Role = "Owner"
				};
				await ctx.AddAsync(store).ConfigureAwait(false);
				await ctx.AddAsync(userStore).ConfigureAwait(false);
				await ctx.SaveChangesAsync().ConfigureAwait(false);
				return store;
			}
		}

		public async Task RemoveStore(string storeId, string userId)
		{
			using(var ctx = _ContextFactory.CreateContext())
			{
				var storeUser = await ctx.UserStore.FirstOrDefaultAsync(o => o.StoreDataId == storeId && o.ApplicationUserId == userId);
				if(storeUser == null)
					return;
				ctx.UserStore.Remove(storeUser);
				await ctx.SaveChangesAsync();
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
