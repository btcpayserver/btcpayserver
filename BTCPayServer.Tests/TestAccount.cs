using BTCPayServer.Controllers;
using BTCPayServer.Models.AccountViewModels;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Servcices.Invoices;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitpayClient;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace BTCPayServer.Tests
{
    public class TestAccount
    {
		ServerTester parent;
		public TestAccount(ServerTester parent)
		{
			this.parent = parent;
			BitPay = new Bitpay(new Key(), parent.PayTester.ServerUri);
		}

		public void GrantAccess()
		{
			GrantAccessAsync().GetAwaiter().GetResult();
		}
		public async Task GrantAccessAsync()
		{
			var extKey = new ExtKey().GetWif(parent.Network);
			var pairingCode = BitPay.RequestClientAuthorization("test", Facade.Merchant);
			var account = parent.PayTester.GetController<AccountController>();
			await account.Register(new RegisterViewModel()
			{
				Email = Guid.NewGuid() + "@toto.com",
				ConfirmPassword = "Kitten0@",
				Password = "Kitten0@",
			});
			UserId = account.RegisteredUserId;

			var store = parent.PayTester.GetController<StoresController>(account.RegisteredUserId);
			await store.CreateStore(new CreateStoreViewModel() { Name = "Test Store" });
			StoreId = store.CreatedStoreId;

			await store.UpdateStore(StoreId, new StoreViewModel()
			{
				DerivationScheme = extKey.Neuter().ToString() + "-[legacy]",
				SpeedPolicy = SpeedPolicy.MediumSpeed
			}, "Save");
			Assert.IsType<ViewResult>(await store.RequestPairing(pairingCode.ToString()));
			await store.Pair(pairingCode.ToString(), StoreId);
		}

		public Bitpay BitPay
		{
			get; set;
		}
		public string UserId
		{
			get; set;
		}

		public string StoreId
		{
			get; set;
		}
	}
}
