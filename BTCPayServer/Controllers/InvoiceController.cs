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
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Validations;

using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Routing;
using NBXplorer.DerivationStrategy;
using NBXplorer;
using BTCPayServer.HostedServices;

namespace BTCPayServer.Controllers
{
    public partial class InvoiceController : Controller
    {
        InvoiceRepository _InvoiceRepository;
        BTCPayWalletProvider _WalletProvider;
        IRateProviderFactory _RateProviders;
        StoreRepository _StoreRepository;
        UserManager<ApplicationUser> _UserManager;
        IFeeProviderFactory _FeeProviderFactory;
        private CurrencyNameTable _CurrencyNameTable;
        EventAggregator _EventAggregator;
        BTCPayNetworkProvider _NetworkProvider;
        ExplorerClientProvider _ExplorerClients;
        public InvoiceController(InvoiceRepository invoiceRepository,
            CurrencyNameTable currencyNameTable,
            UserManager<ApplicationUser> userManager,
            BTCPayWalletProvider walletProvider,
            IRateProviderFactory rateProviders,
            StoreRepository storeRepository,
            EventAggregator eventAggregator,
            BTCPayNetworkProvider networkProvider,
            ExplorerClientProvider explorerClientProviders,
            IFeeProviderFactory feeProviderFactory)
        {
            _ExplorerClients = explorerClientProviders;
            _CurrencyNameTable = currencyNameTable ?? throw new ArgumentNullException(nameof(currencyNameTable));
            _StoreRepository = storeRepository ?? throw new ArgumentNullException(nameof(storeRepository));
            _InvoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
            _WalletProvider = walletProvider ?? throw new ArgumentNullException(nameof(walletProvider));
            _RateProviders = rateProviders ?? throw new ArgumentNullException(nameof(rateProviders));
            _UserManager = userManager;
            _FeeProviderFactory = feeProviderFactory ?? throw new ArgumentNullException(nameof(feeProviderFactory));
            _EventAggregator = eventAggregator;
            _NetworkProvider = networkProvider;
        }


        internal async Task<DataWrapper<InvoiceResponse>> CreateInvoiceCore(Invoice invoice, StoreData store, string serverUrl, double expiryMinutes = 15)
        {
            var derivationStrategies = store.GetDerivationStrategies(_NetworkProvider).ToList();
            if (derivationStrategies.Count == 0)
                throw new BitpayHttpException(400, "This store has not configured the derivation strategy");
            var entity = new InvoiceEntity
            {
                InvoiceTime = DateTimeOffset.UtcNow
            };
            entity.SetDerivationStrategies(derivationStrategies);

            var storeBlob = store.GetStoreBlob();
            Uri notificationUri = Uri.IsWellFormedUriString(invoice.NotificationURL, UriKind.Absolute) ? new Uri(invoice.NotificationURL, UriKind.Absolute) : null;
            if (notificationUri == null || (notificationUri.Scheme != "http" && notificationUri.Scheme != "https")) //TODO: Filer non routable addresses ?
                notificationUri = null;
            EmailAddressAttribute emailValidator = new EmailAddressAttribute();
            entity.ExpirationTime = entity.InvoiceTime.AddMinutes(expiryMinutes);
            entity.MonitoringExpiration = entity.ExpirationTime + TimeSpan.FromMinutes(storeBlob.MonitoringExpiration);
            entity.OrderId = invoice.OrderId;
            entity.ServerUrl = serverUrl;
            entity.FullNotifications = invoice.FullNotifications || invoice.ExtendedNotifications;
            entity.ExtendedNotifications = invoice.ExtendedNotifications;
            entity.NotificationURL = notificationUri?.AbsoluteUri;
            entity.BuyerInformation = Map<Invoice, BuyerInformation>(invoice);
            //Another way of passing buyer info to support
            FillBuyerInfo(invoice.Buyer, entity.BuyerInformation);
            if (entity?.BuyerInformation?.BuyerEmail != null)
            {
                if (!EmailValidator.IsEmail(entity.BuyerInformation.BuyerEmail))
                    throw new BitpayHttpException(400, "Invalid email");
                entity.RefundMail = entity.BuyerInformation.BuyerEmail;
            }
            entity.ProductInformation = Map<Invoice, ProductInformation>(invoice);
            entity.RedirectURL = invoice.RedirectURL ?? store.StoreWebsite;
            entity.Status = "new";
            entity.SpeedPolicy = ParseSpeedPolicy(invoice.TransactionSpeed, store.SpeedPolicy);

            var queries = derivationStrategies
                    .Select(derivationStrategy => ( Wallet: _WalletProvider.GetWallet(derivationStrategy.Network),  
                                                    DerivationStrategy: derivationStrategy.DerivationStrategyBase,
                                                    Network: derivationStrategy.Network,
                                                    RateProvider: _RateProviders.GetRateProvider(derivationStrategy.Network),
                                                    FeeRateProvider: _FeeProviderFactory.CreateFeeProvider(derivationStrategy.Network)))
                    .Where(_ => _.Wallet != null && _.FeeRateProvider != null && _.RateProvider != null)
                    .Select(_ =>
                    {
                        return new
                        {
                            network = _.Network,
                            getFeeRate = _.FeeRateProvider.GetFeeRateAsync(),
                            getRate = _.RateProvider.GetRateAsync(invoice.Currency),
                            getAddress = _.Wallet.ReserveAddressAsync(_.DerivationStrategy)
                        };
                    });

            var cryptoDatas = new Dictionary<string, CryptoData>();
            foreach (var q in queries)
            {
                CryptoData cryptoData = new CryptoData();
                cryptoData.CryptoCode = q.network.CryptoCode;
                cryptoData.FeeRate = (await q.getFeeRate);
                cryptoData.TxFee = storeBlob.NetworkFeeDisabled ? Money.Zero : cryptoData.FeeRate.GetFee(100); // assume price for 100 bytes
                cryptoData.Rate = await q.getRate;
                cryptoData.DepositAddress = (await q.getAddress).ToString();

#pragma warning disable CS0618
                if (q.network.IsBTC)
                {
                    entity.TxFee = cryptoData.TxFee;
                    entity.Rate = cryptoData.Rate;
                    entity.DepositAddress = cryptoData.DepositAddress;
                }
#pragma warning restore CS0618
                cryptoDatas.Add(cryptoData.CryptoCode, cryptoData);
            }
            entity.SetCryptoData(cryptoDatas);
            entity.PosData = invoice.PosData;
            entity = await _InvoiceRepository.CreateInvoiceAsync(store.Id, entity, _NetworkProvider);
            _EventAggregator.Publish(new Events.InvoiceCreatedEvent(entity.Id));
            var resp = entity.EntityToDTO(_NetworkProvider);
            return new DataWrapper<InvoiceResponse>(resp) { Facade = "pos/invoice" };
        }

        private SpeedPolicy ParseSpeedPolicy(string transactionSpeed, SpeedPolicy defaultPolicy)
        {
            if (transactionSpeed == null)
                return defaultPolicy;
            var mappings = new Dictionary<string, SpeedPolicy>();
            mappings.Add("low", SpeedPolicy.LowSpeed);
            mappings.Add("medium", SpeedPolicy.MediumSpeed);
            mappings.Add("high", SpeedPolicy.HighSpeed);
            if (!mappings.TryGetValue(transactionSpeed, out SpeedPolicy policy))
                policy = defaultPolicy;
            return policy;
        }

        private void FillBuyerInfo(Buyer buyer, BuyerInformation buyerInformation)
        {
            if (buyer == null)
                return;
            buyerInformation.BuyerAddress1 = buyerInformation.BuyerAddress1 ?? buyer.Address1;
            buyerInformation.BuyerAddress2 = buyerInformation.BuyerAddress2 ?? buyer.Address2;
            buyerInformation.BuyerCity = buyerInformation.BuyerCity ?? buyer.City;
            buyerInformation.BuyerCountry = buyerInformation.BuyerCountry ?? buyer.country;
            buyerInformation.BuyerEmail = buyerInformation.BuyerEmail ?? buyer.email;
            buyerInformation.BuyerName = buyerInformation.BuyerName ?? buyer.Name;
            buyerInformation.BuyerPhone = buyerInformation.BuyerPhone ?? buyer.phone;
            buyerInformation.BuyerState = buyerInformation.BuyerState ?? buyer.State;
            buyerInformation.BuyerZip = buyerInformation.BuyerZip ?? buyer.zip;
        }

        private DerivationStrategyBase ParseDerivationStrategy(string derivationStrategy, BTCPayNetwork network)
        {
            return new DerivationStrategyFactory(network.NBitcoinNetwork).Parse(derivationStrategy);
        }

        private TDest Map<TFrom, TDest>(TFrom data)
        {
            return JsonConvert.DeserializeObject<TDest>(JsonConvert.SerializeObject(data));
        }
    }
}
