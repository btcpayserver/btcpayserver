using BTCPayServer.Authentication;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Stores;
using BTCPayServer.Wallet;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitpayClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Controllers
{
	[Route("stores")]
	[Authorize(AuthenticationSchemes = "Identity.Application")]
	[Authorize(Policy = "CanAccessStore")]
	public class StoresController : Controller
	{
		public StoresController(
			StoreRepository repo,
			TokenRepository tokenRepo,
			UserManager<ApplicationUser> userManager,
			AccessTokenController tokenController,
			BTCPayWallet wallet,
			IHostingEnvironment env)
		{
			_Repo = repo;
			_TokenRepository = tokenRepo;
			_UserManager = userManager;
			_TokenController = tokenController;
			_Wallet = wallet;
			_Env = env;
		}
		BTCPayWallet _Wallet;
		AccessTokenController _TokenController;
		StoreRepository _Repo;
		TokenRepository _TokenRepository;
		UserManager<ApplicationUser> _UserManager;
		IHostingEnvironment _Env;

		[TempData]
		public string StatusMessage
		{
			get; set;
		}

		[HttpGet]
		[Route("create")]
		public IActionResult CreateStore()
		{
			return View();
		}

		[HttpPost]
		[Route("create")]
		public async Task<IActionResult> CreateStore(CreateStoreViewModel vm)
		{
			if(!ModelState.IsValid)
			{
				return View(vm);
			}
			var store = await _Repo.CreateStore(GetUserId(), vm.Name);
			CreatedStoreId = store.Id;
			StatusMessage = "Store successfully created";
			return RedirectToAction(nameof(ListStores));
		}

		public string CreatedStoreId
		{
			get; set;
		}

		[HttpGet]
		public async Task<IActionResult> ListStores()
		{
			StoresViewModel result = new StoresViewModel();
			result.StatusMessage = StatusMessage;
			var stores = await _Repo.GetStoresByUserId(GetUserId());
			foreach(var store in stores)
			{
				result.Stores.Add(new StoresViewModel.StoreViewModel()
				{
					Id = store.Id,
					Name = store.StoreName,
					WebSite = store.StoreWebsite
				});
			}
			return View(result);
		}

		[HttpGet]
		[Route("{storeId}")]
		public async Task<IActionResult> UpdateStore(string storeId)
		{
			var store = await _Repo.FindStore(storeId, GetUserId());
			if(store == null)
				return NotFound();

			var vm = new StoreViewModel();
			vm.StoreName = store.StoreName;
			vm.StoreWebsite = store.StoreWebsite;
			vm.SpeedPolicy = store.SpeedPolicy;
			vm.ExtPubKey = store.DerivationStrategy;
			vm.StatusMessage = StatusMessage;
			return View(vm);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Route("{storeId}")]
		public async Task<IActionResult> UpdateStore(string storeId, StoreViewModel model)
		{
			if(!ModelState.IsValid)
			{
				return View(model);
			}
			var store = await _Repo.FindStore(storeId, GetUserId());
			if(store == null)
				return NotFound();

			bool needUpdate = false;
			if(store.SpeedPolicy != model.SpeedPolicy)
			{
				needUpdate = true;
				store.SpeedPolicy = model.SpeedPolicy;
			}
			if(store.StoreName != model.StoreName)
			{
				needUpdate = true;
				store.StoreName = model.StoreName;
			}
			if(store.StoreWebsite != model.StoreWebsite)
			{
				needUpdate = true;
				store.StoreWebsite = model.StoreWebsite;
			}

			if(store.DerivationStrategy != model.ExtPubKey)
			{
				needUpdate = true;
				try
				{
					await _Wallet.TrackAsync(model.ExtPubKey);
					store.DerivationStrategy = model.ExtPubKey;
				}
				catch
				{
					ModelState.AddModelError(nameof(model.ExtPubKey), "Invalid Derivation Scheme");
					return View(model);
				}
			}

			if(needUpdate)
			{
				await _Repo.UpdateStore(store);
				StatusMessage = "Store successfully updated";
			}

			return RedirectToAction(nameof(UpdateStore), new
			{
				storeId = storeId
			});
		}

		[HttpGet]
		[Route("{storeId}/Tokens")]
		public async Task<IActionResult> ListTokens(string storeId)
		{
			var model = new TokensViewModel();
			var tokens = await _TokenRepository.GetTokensByPairedIdAsync(storeId);
			model.StatusMessage = StatusMessage;
			model.Tokens = tokens.Select(t => new TokenViewModel()
			{
				Facade = t.Name,
				Label = t.Label,
				SIN = t.SIN,
				Id = t.Value
			}).ToArray();
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Route("{storeId}/Tokens/Create")]
		public async Task<IActionResult> CreateToken(string storeId, CreateTokenViewModel model)
		{
			if(!ModelState.IsValid)
			{
				return View(model);
			}

			var pairingCode = await _TokenController.GetPairingCode(new PairingCodeRequest()
			{
				Facade = model.Facade,
				Label = model.Label,
				Id = NBitpayClient.Extensions.BitIdExtensions.GetBitIDSIN(new PubKey(model.PublicKey))
			});
			
			return RedirectToAction(nameof(RequestPairing), new
			{
				pairingCode = pairingCode.Data[0].PairingCode,
				selectedStore = storeId
			});
		}

		[HttpGet]
		[Route("{storeId}/Tokens/Create")]
		public IActionResult CreateToken()
		{
			var model = new CreateTokenViewModel();
			model.Facade = "merchant";
			if(_Env.IsDevelopment())
			{
				model.PublicKey = new Key().PubKey.ToHex();
			}
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Route("{storeId}/Tokens/Delete")]
		public async Task<IActionResult> DeleteToken(string storeId, string name, string sin)
		{
			if(await _TokenRepository.DeleteToken(sin, name, storeId))
				StatusMessage = "Token revoked";
			else
				StatusMessage = "Failure to revoke this token";
			return RedirectToAction(nameof(ListTokens));
		}


		[HttpGet]
		[Route("/api-access-request")]
		public async Task<IActionResult> RequestPairing(string pairingCode, string selectedStore = null)
		{
			var pairing = await _TokenRepository.GetPairingAsync(pairingCode);
			if(pairing == null)
			{
				StatusMessage = "Unknown pairing code";
				return RedirectToAction(nameof(ListStores));
			}
			else
			{
				var stores = await _Repo.GetStoresByUserId(GetUserId());
				return View(new PairingModel()
				{
					Id = pairing.Id,
					Facade = pairing.Facade,
					Label = pairing.Label,
					SIN = pairing.SIN,
					SelectedStore = selectedStore ?? stores.FirstOrDefault()?.Id,
					Stores = stores.Select(s => new PairingModel.StoreViewModel()
					{
						Id = s.Id,
						Name = string.IsNullOrEmpty(s.StoreName) ? s.Id : s.StoreName
					}).ToArray()
				});
			}
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Route("api-access-request")]
		public async Task<IActionResult> Pair(string pairingCode, string selectedStore)
		{
			var store = await _Repo.FindStore(selectedStore, GetUserId());
			if(store == null)
				return NotFound();
			if(pairingCode != null && await _TokenRepository.PairWithAsync(pairingCode, store.Id))
			{
				StatusMessage = "Pairing is successfull";
				return RedirectToAction(nameof(ListTokens), new
				{
					storeId = store.Id
				});
			}
			else
			{
				StatusMessage = "Pairing failed";
				return RedirectToAction(nameof(ListTokens), new
				{
					storeId = store.Id
				});
			}
		}

		private string GetUserId()
		{
			return _UserManager.GetUserId(User);
		}
	}
}
