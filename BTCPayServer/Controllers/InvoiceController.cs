using BTCPayServer.Authentication;
using System.Reflection;
using System.Linq;
using Microsoft.Extensions.Logging;
using BTCPayServer.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitpayClient;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Models;
using Newtonsoft.Json;
using System.Globalization;
using NBitcoin;
using NBitcoin.DataEncoders;
using BTCPayServer.Filters;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NBitcoin.Payment;
using BTCPayServer.Data;
using BTCPayServer.Models.InvoicingModels;
using System.Security.Claims;
using BTCPayServer.Services;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using BTCPayServer.Services.Stores;
using BTCPayServer.Servcices.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Wallets;

namespace BTCPayServer.Controllers
{
	public partial class InvoiceController : Controller
	{
		TokenRepository _TokenRepository;
		InvoiceRepository _InvoiceRepository;
		IExternalUrlProvider _ExternalUrl;
		BTCPayWallet _Wallet;
		IRateProvider _RateProvider;
		private InvoiceWatcher _Watcher;
		StoreRepository _StoreRepository;
		Network _Network;
		UserManager<ApplicationUser> _UserManager;
		IFeeProvider _FeeProvider;

		public InvoiceController(
			Network network,
			InvoiceRepository invoiceRepository,
			UserManager<ApplicationUser> userManager,
			TokenRepository tokenRepository,
			BTCPayWallet wallet,
			IExternalUrlProvider externalUrl,
			IRateProvider rateProvider,
			StoreRepository storeRepository,
			InvoiceWatcher watcher,
			IFeeProvider feeProvider)
		{
			_StoreRepository = storeRepository ?? throw new ArgumentNullException(nameof(storeRepository));
			_Network = network ?? throw new ArgumentNullException(nameof(network));
			_TokenRepository = tokenRepository ?? throw new ArgumentNullException(nameof(tokenRepository));
			_InvoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
			_ExternalUrl = externalUrl;
			_Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
			_RateProvider = rateProvider ?? throw new ArgumentNullException(nameof(rateProvider));
			_Watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
			_UserManager = userManager;
			_FeeProvider = feeProvider ?? throw new ArgumentNullException(nameof(feeProvider));
		}

		static Regex _Email;
		bool IsEmail(string str)
		{
			if(String.IsNullOrWhiteSpace(str))
				return false;
			if(_Email == null)
				_Email = new Regex("^((([a-z]|\\d|[!#\\$%&'\\*\\+\\-\\/=\\?\\^_`{\\|}~]|[\\u00A0-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFEF])+(\\.([a-z]|\\d|[!#\\$%&'\\*\\+\\-\\/=\\?\\^_`{\\|}~]|[\\u00A0-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFEF])+)*)|((\\x22)((((\\x20|\\x09)*(\\x0d\\x0a))?(\\x20|\\x09)+)?(([\\x01-\\x08\\x0b\\x0c\\x0e-\\x1f\\x7f]|\\x21|[\\x23-\\x5b]|[\\x5d-\\x7e]|[\\u00A0-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFEF])|(\\\\([\\x01-\\x09\\x0b\\x0c\\x0d-\\x7f]|[\\u00A0-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFEF]))))*(((\\x20|\\x09)*(\\x0d\\x0a))?(\\x20|\\x09)+)?(\\x22)))@((([a-z]|\\d|[\\u00A0-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFEF])|(([a-z]|\\d|[\\u00A0-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFEF])([a-z]|\\d|-|\\.|_|~|[\\u00A0-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFEF])*([a-z]|\\d|[\\u00A0-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFEF])))\\.)+(([a-z]|[\\u00A0-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFEF])|(([a-z]|[\\u00A0-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFEF])([a-z]|\\d|-|\\.|_|~|[\\u00A0-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFEF])*([a-z]|[\\u00A0-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFEF])))\\.?$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.Compiled, TimeSpan.FromSeconds(2.0));
			return _Email.IsMatch(str);
		}

		private async Task<DataWrapper<InvoiceResponse>> CreateInvoiceCore(Invoice invoice, StoreData store)
		{
			var derivationStrategy = store.DerivationStrategy;
			var entity = new InvoiceEntity
			{
				InvoiceTime = DateTimeOffset.UtcNow,
				DerivationStrategy = derivationStrategy ?? throw new BitpayHttpException(400, "This store has not configured the derivation strategy")
			};
			EmailAddressAttribute emailValidator = new EmailAddressAttribute();
			entity.ExpirationTime = entity.InvoiceTime + TimeSpan.FromMinutes(15.0);
			entity.BuyerInformation = Map<Invoice, BuyerInformation>(invoice);
			entity.RefundMail = IsEmail(entity?.BuyerInformation?.BuyerEmail) ? entity.BuyerInformation.BuyerEmail : null;
			entity.ProductInformation = Map<Invoice, ProductInformation>(invoice);
			entity.RedirectURL = invoice.RedirectURL ?? store.StoreWebsite;
			entity.Status = "new";
			entity.SpeedPolicy = store.SpeedPolicy;
			entity.TxFee = (await _FeeProvider.GetFeeRateAsync()).GetFee(100); // assume price for 100 bytes
			entity.Rate = (double)await _RateProvider.GetRateAsync(invoice.Currency);
			entity.PosData = invoice.PosData;
			entity.DepositAddress = await _Wallet.ReserveAddressAsync(derivationStrategy);

			entity = await _InvoiceRepository.CreateInvoiceAsync(store.Id, entity);
			await _Wallet.MapAsync(entity.DepositAddress, entity.Id);
			await _Watcher.WatchAsync(entity.Id);
			var resp = EntityToDTO(entity);
			return new DataWrapper<InvoiceResponse>(resp) { Facade = "pos/invoice" };
		}

		private InvoiceResponse EntityToDTO(InvoiceEntity entity)
		{
			InvoiceResponse dto = new InvoiceResponse
			{
				Id = entity.Id,
				OrderId = entity.OrderId,
				PosData = entity.PosData,
				CurrentTime = DateTimeOffset.UtcNow,
				InvoiceTime = entity.InvoiceTime,
				ExpirationTime = entity.ExpirationTime,
				BTCPrice = Money.Coins((decimal)(1.0 / entity.Rate)).ToString(),
				Status = entity.Status,
				Url = _ExternalUrl.GetAbsolute("invoice?id=" + entity.Id),
				Currency = entity.ProductInformation.Currency,
				Flags = new Flags() { Refundable = entity.Refundable }
			};
			Populate(entity.ProductInformation, dto);
			Populate(entity.BuyerInformation, dto);
			dto.ExRates = new Dictionary<string, double>
			{
				{ entity.ProductInformation.Currency, entity.Rate }
			};
			dto.PaymentUrls = new InvoicePaymentUrls()
			{
				BIP72 = $"bitcoin:{entity.DepositAddress}?amount={entity.GetCryptoDue()}&r={_ExternalUrl.GetAbsolute($"i/{entity.Id}")}",
				BIP72b = $"bitcoin:?r={_ExternalUrl.GetAbsolute($"i/{entity.Id}")}",
				BIP73 = _ExternalUrl.GetAbsolute($"i/{entity.Id}"),
				BIP21 = $"bitcoin:{entity.DepositAddress}?amount={entity.GetCryptoDue()}",
			};
			dto.BitcoinAddress = entity.DepositAddress.ToString();
			dto.Token = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16)); //No idea what it is useful for
			dto.Guid = Guid.NewGuid().ToString();

			var paid = entity.Payments.Select(p => p.Output.Value).Sum();
			dto.BTCPaid = paid.ToString();
			dto.BTCDue = entity.GetCryptoDue().ToString();
			dto.ExceptionStatus = entity.ExceptionStatus == null ? new JValue(false) : new JValue(entity.ExceptionStatus);
			return dto;
		}

		private TDest Map<TFrom, TDest>(TFrom data)
		{
			return JsonConvert.DeserializeObject<TDest>(JsonConvert.SerializeObject(data));
		}
		private void Populate<TFrom, TDest>(TFrom from, TDest dest)
		{
			var str = JsonConvert.SerializeObject(from);
			JsonConvert.PopulateObject(str, dest);
		}

	
	}
}
