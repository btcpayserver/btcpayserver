using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;

namespace BTCPayServer.Services.Rates
{
    [Function("buyPrice", "uint256")]
    public class BuyPrice : FunctionMessage
    {
    }

    public class BNBUSDResult
    {
        public decimal btc { get; set; }
    }

    public class BPROSUSRateProvider : IRateProvider
    {
        private readonly HttpClient Client;

        public BPROSUSRateProvider(IHttpClientFactory httpClientFactory)
        {
            Client = httpClientFactory.CreateClient();
            Client.BaseAddress = new Uri("https://api.coingecko.com/api/v3/simple/");
            Client.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            DateTimeOffset start = DateTimeOffset.Now;

            // var buyPriceFunction = new BuyPrice();
            // var web3 = new Web3("https://data-seed-prebsc-1-s1.binance.org:8545/", null, null);
            // var buyPriceHandler = web3.Eth.GetContractQueryHandler<BuyPrice>();
            //
            // var price = await buyPriceHandler.QueryAsync<BigInteger>("0xb0D86E2C31b153FfF675c449226777Cc4ddcd177", buyPriceFunction);
            // var value = Web3.Convert.FromWei(price, UnitConversion.EthUnit.Ether);
            //
            // var response =
            //     await Client.GetAsync(
            //         "price?ids=binancecoin&vs_currencies=BTC",
            //         cancellationToken);
            // var usd = JObject.Parse(await response.Content.ReadAsStringAsync()).GetValue("binancecoin").ToObject<BNBUSDResult>();

            var usd = 0.012;

            return new List<PairRate>
            {
                //new PairRate(new CurrencyPair("BPROSUS", "BTC"), new BidAsk(value * usd.btc))
                new PairRate(new CurrencyPair("BPROSUS", "BTC"), new BidAsk(new Decimal(0.0000004084868210771281)))
            }.ToArray();

            TimeSpan diff = start - DateTimeOffset.Now;
            Console.Out.WriteLine("Time to fetch bProsus rate: " + diff.Seconds);

            // int precision = 10000000;
            // var getAmountsOutFunctionMessage = new GetAmountsOutFunction()
            // {
            //     AmountIn = precision,
            //     Path = new List<string>{"0xCDfd3D7817F9402e58a428CF304Cb7493e98336D", "0xe9e7cea3dedca5984780bafc599bd69add087d56"}
            // };
            //
            // var web3 = new Web3("https://data-seed-prebsc-1-s1.binance.org:8545/");
            // var result = await web3.Eth.GetContractQueryHandler<GetAmountsOutFunction>()//;//GetContractHandler("0xb0D86E2C31b153FfF675c449226777Cc4ddcd177")
            //     .QueryAsync<List<BigInteger>>("0xCDfd3D7817F9402e58a428CF304Cb7493e98336D", getAmountsOutFunctionMessage);
            // throw new System.NotImplementedException();
        }
    }
}
