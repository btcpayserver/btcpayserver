using BTCPayServer.Authentication;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitpayClient;
using NBXplorer.DerivationStrategy;
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
			CallbackController callbackController,
			UserManager<ApplicationUser> userManager,
			AccessTokenController tokenController,
			BTCPayWallet wallet,
			Network network,
			IHostingEnvironment env)
		{
			_Repo = repo;
			_TokenRepository = tokenRepo;
			_UserManager = userManager;
			_TokenController = tokenController;
			_Wallet = wallet;
			_Env = env;
			_Network = network;
			_CallbackController = callbackController;
		}
		Network _Network;
		CallbackController _CallbackController;
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
			var balances = stores.Select(async s => string.IsNullOrEmpty(s.DerivationStrategy) ? Money.Zero : await _Wallet.GetBalance(ParseDerivationStrategy(s.DerivationStrategy))).ToArray();

			for(int i = 0; i < stores.Length; i++)
			{
				var store = stores[i];
				result.Stores.Add(new StoresViewModel.StoreViewModel()
				{
					Id = store.Id,
					Name = store.StoreName,
					WebSite = store.StoreWebsite,
					Balance = await balances[i]
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
			vm.DerivationScheme = store.DerivationStrategy;
			vm.StatusMessage = StatusMessage;
			return View(vm);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Route("{storeId}")]
		public async Task<IActionResult> UpdateStore(string storeId, StoreViewModel model, string command)
		{
			if(!ModelState.IsValid)
			{
				return View(model);
			}
			var store = await _Repo.FindStore(storeId, GetUserId());
			if(store == null)
				return NotFound();

			if(command == "Save")
			{
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

				if(store.DerivationStrategy != model.DerivationScheme)
				{
					needUpdate = true;
					try
					{
						var strategy = ParseDerivationStrategy(model.DerivationScheme);
						await _Wallet.TrackAsync(strategy);
						await _CallbackController.RegisterCallbackUriAsync(strategy, Request);
						store.DerivationStrategy = model.DerivationScheme;
					}
					catch
					{
						ModelState.AddModelError(nameof(model.DerivationScheme), "Invalid Derivation Scheme");
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
			else
			{
				var facto = new DerivationStrategyFactory(_Network);
				var scheme = facto.Parse(model.DerivationScheme);
				var line = scheme.GetLineFor(DerivationFeature.Deposit);

				for(int i = 0; i < 10; i++)
				{
					var address = line.Derive((uint)i);
					model.AddressSamples.Add((line.Path.Derive((uint)i).ToString(), address.ScriptPubKey.GetDestinationAddress(_Network).ToString()));
				}
				return View(model);
			}
		}

		private DerivationStrategyBase ParseDerivationStrategy(string derivationScheme)
		{
			return new DerivationStrategyFactory(_Network).Parse(derivationScheme);
		}

		[HttpGet]
		[Route("{storeId}/Tokens")]
		public async Task<IActionResult> ListTokens(string storeId)
		{
			var model = new TokensViewModel();
			var tokens = await _TokenRepository.GetTokensByStoreIdAsync(storeId);
			model.StatusMessage = StatusMessage;
			model.Tokens = tokens.Select(t => new TokenViewModel()
			{
				Facade = t.Facade,
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

			var tokenRequest = new TokenRequest()
			{
				Facade = model.Facade,
				Label = model.Label,
				Id = model.PublicKey == null ? null : NBitpayClient.Extensions.BitIdExtensions.GetBitIDSIN(new PubKey(model.PublicKey))
			};

			string pairingCode = null;
			if(model.PublicKey == null)
			{
				tokenRequest.PairingCode = await _TokenRepository.CreatePairingCodeAsync();
				await _TokenRepository.UpdatePairingCode(new PairingCodeEntity()
				{
					Id = tokenRequest.PairingCode,
					Facade = model.Facade,
					Label = model.Label,
				});
				await _TokenRepository.PairWithStoreAsync(tokenRequest.PairingCode, storeId);
				pairingCode = tokenRequest.PairingCode;
			}
			else
			{
				pairingCode = ((DataWrapper<List<PairingCodeResponse>>)await _TokenController.Tokens(tokenRequest)).Data[0].PairingCode;
			}

			return RedirectToAction(nameof(RequestPairing), new
			{
				pairingCode = pairingCode,
				selectedStore = storeId
			});
		}

		[HttpGet]
		[Route("{storeId}/Tokens/Create")]
		public IActionResult CreateToken()
		{
			var model = new CreateTokenViewModel();
			model.Facade = "merchant";
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Route("{storeId}/Tokens/Delete")]
		public async Task<IActionResult> DeleteToken(string storeId, string tokenId)
		{
			var token = await _TokenRepository.GetToken(tokenId);
			if(token == null ||
				token.StoreId != storeId ||
			   !await _TokenRepository.DeleteToken(tokenId))
				StatusMessage = "Failure to revoke this token";
			else
				StatusMessage = "Token revoked";
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
					SIN = pairing.SIN ?? "Server-Initiated Pairing",
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
			var pairing = await _TokenRepository.GetPairingAsync(pairingCode);
			if(store == null || pairing == null)
				return NotFound();
			if(pairingCode != null && await _TokenRepository.PairWithStoreAsync(pairingCode, store.Id))
			{
				StatusMessage = "Pairing is successfull";
				if(pairing.SIN == null)
					StatusMessage = "Server initiated pairing code: " + pairingCode;
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
