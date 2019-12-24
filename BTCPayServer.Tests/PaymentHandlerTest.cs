using BTCPayServer.Tests.Logging;
using NBitcoin;
using Xunit;
using Xunit.Abstractions;
using BTCPayServer.Data;
using BTCPayServer.Services.Rates;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Rating;

namespace BTCPayServer.Tests
{
    [Trait("Fast", "Fast")]
    public class PaymentHandlerTest
    {
        private BitcoinLikePaymentHandler handlerBTC;
        private LightningLikePaymentHandler handlerLN;
        private Dictionary<CurrencyPair, Task<RateResult>> currencyPairRateResult;
        
        public PaymentHandlerTest(ITestOutputHelper helper)
        {
 
#pragma warning disable CS0618            
            
            Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
            Logs.LogProvider = new XUnitLogProvider(helper);
            
            var dummy = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.RegTest).ToString();
            var networkProvider = new BTCPayNetworkProvider(NetworkType.Regtest);

            currencyPairRateResult = new Dictionary<CurrencyPair, Task<RateResult>>();
            
            var rateResultUSDBTC = new RateResult();
            rateResultUSDBTC.BidAsk= new BidAsk(1m);

            var rateResultBTCUSD = new RateResult();
            rateResultBTCUSD.BidAsk= new BidAsk(1m);
            
            currencyPairRateResult.Add(new CurrencyPair("USD", "BTC"), Task.FromResult(rateResultUSDBTC));
            currencyPairRateResult.Add(new CurrencyPair("BTC", "USD"), Task.FromResult(rateResultBTCUSD));
 
            handlerBTC = new BitcoinLikePaymentHandler(null, networkProvider, null, null);
            handlerLN = new LightningLikePaymentHandler(null, null, networkProvider, null);
            
#pragma warning restore CS0618               
        }

        [Fact]
        public void CanPayWithLightningWhenInvoiceTotalUnderLightningMaxValue()
        {
            
#pragma warning disable CS0618
            
            //Given
            var store = new StoreBlob
            {
                OnChainMinValue = null,
                LightningMaxValue = new CurrencyValue() {Value = 100.00m, Currency = "USD"}
            };
            var paymentMethodId = new PaymentMethodId("BTC", PaymentTypes.LightningLike);
            
            //When
            var totalInvoiceAmount = new Money(98m, MoneyUnit.BTC);
            
           
            //Then
            var errorMessage = handlerLN.IsPaymentMethodAllowedBasedOnInvoiceAmount(store, currencyPairRateResult,
                totalInvoiceAmount, paymentMethodId);
            
            Assert.Equal(errorMessage.Result, string.Empty);

#pragma warning restore CS0618     
        }

    
        [Fact]
        public void CannotPayWithLightningWhenInvoiceTotalAboveLightningMaxValue()
        {
            
#pragma warning disable CS0618
            
            //Given
            var store = new StoreBlob
            {
                OnChainMinValue = null,
                LightningMaxValue = new CurrencyValue() {Value = 100.00m, Currency = "USD"}
            };
            var totalInvoiceAmount = new Money(102m, MoneyUnit.BTC);
            
            //When
            var paymentMethodId = new PaymentMethodId("BTC", PaymentTypes.LightningLike);
           
            //Then
            var errorMessage = handlerLN.IsPaymentMethodAllowedBasedOnInvoiceAmount(store, currencyPairRateResult,
                totalInvoiceAmount, paymentMethodId);
            
            Assert.NotEqual(errorMessage.Result, string.Empty);

#pragma warning restore CS0618     
        }    
        
        [Fact]
        public void CanPayWithLightningWhenInvoiceTotalEqualLightningMaxValue()
        {
            
#pragma warning disable CS0618
            
            //Given
            var store = new StoreBlob
            {
                OnChainMinValue = null,
                LightningMaxValue = new CurrencyValue() {Value = 100.00m, Currency = "USD"}
            };
            var paymentMethodId = new PaymentMethodId("BTC", PaymentTypes.LightningLike);
            
            //When
            var totalInvoiceAmount = new Money(100m, MoneyUnit.BTC);
           
            //Then
            var errorMessage = handlerLN.IsPaymentMethodAllowedBasedOnInvoiceAmount(store, currencyPairRateResult,
                totalInvoiceAmount, paymentMethodId);
            
            Assert.Equal(errorMessage.Result, string.Empty);

#pragma warning restore CS0618     
        }

        [Fact]
        public void CanPayWithBitcoinWhenInvoiceTotalAboveOnChainMinValue()
        {
            
#pragma warning disable CS0618
            
            //Given
            var store = new StoreBlob
            {
                OnChainMinValue = new CurrencyValue() {Value = 100.00m, Currency = "USD"},
                LightningMaxValue = null
            };
            var paymentMethodId = new PaymentMethodId("BTC", PaymentTypes.BTCLike);
            
            //When
            var totalInvoiceAmount = new Money(105m, MoneyUnit.BTC);
            
           
            //Then
            var errorMessage = handlerBTC.IsPaymentMethodAllowedBasedOnInvoiceAmount(store, currencyPairRateResult,
                totalInvoiceAmount, paymentMethodId);
            
            Assert.Equal(errorMessage.Result, string.Empty);

#pragma warning restore CS0618     
        }

    
        [Fact]
        public void CannotPayWithBitcoinWhenInvoiceTotalUnderOnChainMinValue()
        {
            
#pragma warning disable CS0618
            
            //Given
            var store = new StoreBlob
            {
                OnChainMinValue = new CurrencyValue() {Value = 100.00m, Currency = "USD"},
                LightningMaxValue = null
            };
            var totalInvoiceAmount = new Money(98m, MoneyUnit.BTC);
            
            //When
            var paymentMethodId = new PaymentMethodId("BTC", PaymentTypes.BTCLike);
           
            //Then
            var errorMessage = handlerBTC.IsPaymentMethodAllowedBasedOnInvoiceAmount(store, currencyPairRateResult,
                totalInvoiceAmount, paymentMethodId);
            
            Assert.NotEqual(errorMessage.Result, string.Empty);

#pragma warning restore CS0618     
        }    
        
        [Fact]
        public void CanPayWithBitcoinWhenInvoiceTotalEqualOnChainMinValue()
        {
            
#pragma warning disable CS0618
            
            //Given
            var store = new StoreBlob
            {
                OnChainMinValue = new CurrencyValue() {Value = 100.00m, Currency = "USD"},
                LightningMaxValue = null
            };
            var paymentMethodId = new PaymentMethodId("BTC", PaymentTypes.BTCLike);
            
            //When
            var totalInvoiceAmount = new Money(100m, MoneyUnit.BTC);
           
            //Then
            var errorMessage = handlerBTC.IsPaymentMethodAllowedBasedOnInvoiceAmount(store, currencyPairRateResult,
                totalInvoiceAmount, paymentMethodId);
            
            Assert.Equal(errorMessage.Result, string.Empty);

#pragma warning restore CS0618     
        }
   
        
        [Fact]
        public void CannotPayWithBitcoinWhenInvoiceTotalUnderOnChainMinValueWhenLightningMaxValueIsGreater()
        {
            
#pragma warning disable CS0618
            
            //Given
            var store = new StoreBlob
            {
                OnChainMinValue = new CurrencyValue() {Value = 50.00m, Currency = "USD"},
                LightningMaxValue = new CurrencyValue() {Value = 100.00m, Currency = "USD"}
            };
            var paymentMethodId = new PaymentMethodId("BTC", PaymentTypes.BTCLike);
            
            //When
            var totalInvoiceAmount = new Money(45m, MoneyUnit.BTC);
           
            //Then
            var errorMessage = handlerBTC.IsPaymentMethodAllowedBasedOnInvoiceAmount(store, currencyPairRateResult,
                totalInvoiceAmount, paymentMethodId);
            
            Assert.NotEqual(errorMessage.Result, string.Empty);

#pragma warning restore CS0618     
        }  
        
        
    }
}
