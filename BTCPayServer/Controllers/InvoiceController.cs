﻿using BTCPayServer.Authentication;
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
using BTCPayServer.Payments;

namespace BTCPayServer.Controllers
{
    public partial class InvoiceController : Controller
    {
        InvoiceRepository _InvoiceRepository;
        IRateProviderFactory _RateProviders;
        StoreRepository _StoreRepository;
        UserManager<ApplicationUser> _UserManager;
        private CurrencyNameTable _CurrencyNameTable;
        EventAggregator _EventAggregator;
        BTCPayNetworkProvider _NetworkProvider;
        private readonly BTCPayWalletProvider _WalletProvider;
        IServiceProvider _ServiceProvider;
        public InvoiceController(
            IServiceProvider serviceProvider,
            InvoiceRepository invoiceRepository,
            CurrencyNameTable currencyNameTable,
            UserManager<ApplicationUser> userManager,
            IRateProviderFactory rateProviders,
            StoreRepository storeRepository,
            EventAggregator eventAggregator,
            BTCPayWalletProvider walletProvider,
            BTCPayNetworkProvider networkProvider)
        {
            _ServiceProvider = serviceProvider;
            _CurrencyNameTable = currencyNameTable ?? throw new ArgumentNullException(nameof(currencyNameTable));
            _StoreRepository = storeRepository ?? throw new ArgumentNullException(nameof(storeRepository));
            _InvoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
            _RateProviders = rateProviders ?? throw new ArgumentNullException(nameof(rateProviders));
            _UserManager = userManager;
            _EventAggregator = eventAggregator;
            _NetworkProvider = networkProvider;
            _WalletProvider = walletProvider;
        }


        internal async Task<DataWrapper<InvoiceResponse>> CreateInvoiceCore(Invoice invoice, StoreData store, string serverUrl)
        {
            var entity = new InvoiceEntity
            {
                InvoiceTime = DateTimeOffset.UtcNow
            };

            var storeBlob = store.GetStoreBlob();
            Uri notificationUri = Uri.IsWellFormedUriString(invoice.NotificationURL, UriKind.Absolute) ? new Uri(invoice.NotificationURL, UriKind.Absolute) : null;
            if (notificationUri == null || (notificationUri.Scheme != "http" && notificationUri.Scheme != "https")) //TODO: Filer non routable addresses ?
                notificationUri = null;
            EmailAddressAttribute emailValidator = new EmailAddressAttribute();
            entity.ExpirationTime = entity.InvoiceTime.AddMinutes(storeBlob.InvoiceExpiration);
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


            var supportedPaymentMethods = store.GetSupportedPaymentMethods(_NetworkProvider)
                                               .Select(c =>
                                                (Handler: (IPaymentMethodHandler)_ServiceProvider.GetService(typeof(IPaymentMethodHandler<>).MakeGenericType(c.GetType())),
                                                SupportedPaymentMethod: c,
                                                Network: _NetworkProvider.GetNetwork(c.PaymentId.CryptoCode)))
                                                .Where(c => c.Network != null)
                                                .Select(o =>
                                                    (SupportedPaymentMethod: o.SupportedPaymentMethod,
                                                    PaymentMethod: CreatePaymentMethodAsync(o.Handler, o.SupportedPaymentMethod, o.Network, entity, storeBlob)))
                                                .ToList();

            List<string> paymentMethodErrors = new List<string>();
            List<ISupportedPaymentMethod> supported = new List<ISupportedPaymentMethod>();
            var paymentMethods = new PaymentMethodDictionary();
            foreach (var o in supportedPaymentMethods)
            {
                try
                {
                    var paymentMethod = await o.PaymentMethod;
                    if (paymentMethod == null)
                        throw new PaymentMethodUnavailableException("Payment method unavailable (The handler returned null)");
                    supported.Add(o.SupportedPaymentMethod);
                    paymentMethods.Add(paymentMethod);
                }
                catch (PaymentMethodUnavailableException ex)
                {
                    paymentMethodErrors.Add($"{o.SupportedPaymentMethod.PaymentId.CryptoCode} ({o.SupportedPaymentMethod.PaymentId.PaymentType}): Payment method unavailable ({ex.Message})");
                }
                catch (Exception ex)
                {
                    paymentMethodErrors.Add($"{o.SupportedPaymentMethod.PaymentId.CryptoCode} ({o.SupportedPaymentMethod.PaymentId.PaymentType}): Unexpected exception ({ex.ToString()})");
                }
            }

            if (supported.Count == 0)
            {
                StringBuilder errors = new StringBuilder();
                errors.AppendLine("No payment method available for this store");
                foreach(var error in paymentMethodErrors)
                {
                    errors.AppendLine(error);
                }
                throw new BitpayHttpException(400, errors.ToString());
            }

            entity.SetSupportedPaymentMethods(supported);
            entity.SetPaymentMethods(paymentMethods);
#pragma warning disable CS0618
            // Legacy Bitpay clients expect information for BTC information, even if the store do not support it
            var legacyBTCisSet = paymentMethods.Any(p => p.GetId().IsBTCOnChain);
            if (!legacyBTCisSet && _NetworkProvider.BTC != null)
            {
                var btc = _NetworkProvider.BTC;
                var feeProvider = ((IFeeProviderFactory)_ServiceProvider.GetService(typeof(IFeeProviderFactory))).CreateFeeProvider(btc);
                var rateProvider = storeBlob.ApplyRateRules(btc, _RateProviders.GetRateProvider(btc, false));
                if (feeProvider != null && rateProvider != null)
                {
                    var gettingFee = feeProvider.GetFeeRateAsync();
                    var gettingRate = rateProvider.GetRateAsync(invoice.Currency);
                    entity.TxFee = GetTxFee(storeBlob, await gettingFee);
                    entity.Rate = await gettingRate;
                }
#pragma warning restore CS0618
            }
            entity.PosData = invoice.PosData;
            entity = await _InvoiceRepository.CreateInvoiceAsync(store.Id, entity, paymentMethodErrors, _NetworkProvider);

            _EventAggregator.Publish(new Events.InvoiceEvent(entity, 1001, "invoice_created"));
            var resp = entity.EntityToDTO(_NetworkProvider);
            return new DataWrapper<InvoiceResponse>(resp) { Facade = "pos/invoice" };
        }

        private async Task<PaymentMethod> CreatePaymentMethodAsync(IPaymentMethodHandler handler, ISupportedPaymentMethod supportedPaymentMethod, BTCPayNetwork network, InvoiceEntity entity, StoreBlob storeBlob)
        {
            var rate = await storeBlob.ApplyRateRules(network, _RateProviders.GetRateProvider(network, false)).GetRateAsync(entity.ProductInformation.Currency);
            PaymentMethod paymentMethod = new PaymentMethod();
            paymentMethod.ParentEntity = entity;
            paymentMethod.Network = network;
            paymentMethod.SetId(supportedPaymentMethod.PaymentId);
            paymentMethod.Rate = rate;
            var paymentDetails = await handler.CreatePaymentMethodDetails(supportedPaymentMethod, paymentMethod, network);
            if (storeBlob.NetworkFeeDisabled)
                paymentDetails.SetNoTxFee();
            paymentMethod.SetPaymentMethodDetails(paymentDetails);

            // Check if Lightning Max value is exceeded
            if (supportedPaymentMethod.PaymentId.PaymentType == PaymentTypes.LightningLike &&
               storeBlob.LightningMaxValue != null)
            {
                var lightningMaxValue = storeBlob.LightningMaxValue;
                var lightningMaxValueRate = 0.0m;
                if (lightningMaxValue.Currency == entity.ProductInformation.Currency)
                    lightningMaxValueRate = paymentMethod.Rate;
                else
                    lightningMaxValueRate = await storeBlob.ApplyRateRules(network, _RateProviders.GetRateProvider(network, false)).GetRateAsync(lightningMaxValue.Currency);

                var lightningMaxValueCrypto = Money.Coins(lightningMaxValue.Value / lightningMaxValueRate);
                if (paymentMethod.Calculate().Due > lightningMaxValueCrypto)
                {
                    throw new PaymentMethodUnavailableException("Lightning max value exceeded");
                }
            }
            ///////////////


#pragma warning disable CS0618
            if (paymentMethod.GetId().IsBTCOnChain)
            {
                entity.TxFee = paymentMethod.TxFee;
                entity.Rate = paymentMethod.Rate;
                entity.DepositAddress = paymentMethod.DepositAddress;
            }
#pragma warning restore CS0618
            return paymentMethod;
        }

#pragma warning disable CS0618
        private static Money GetTxFee(StoreBlob storeBlob, FeeRate feeRate)
        {
            return storeBlob.NetworkFeeDisabled ? Money.Zero : feeRate.GetFee(100);
        }
#pragma warning restore CS0618

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
