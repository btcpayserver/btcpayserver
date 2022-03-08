using System.Net;
using System.Text;

namespace BTCPayServer.Plugins.Custodians.Kraken.Kraken;

public class KrakenMockHttpMessageHandler : HttpMessageHandler
{
    public const string NewWithdrawalId = "NEW-WITHDRAWAL-ID";
    public const string BadWithdrawalId = "BAD-WITHDRAWAL-ID";
    public static readonly decimal WithdrawalAmountExclFee = new decimal(0.005);
    public static readonly decimal ExpectedWithdrawalFee = new decimal(0.00002);
    public const string TargetWithdrawalAddress = "bc1q01234567891234567891234567891234567890";


    public const string GoodFiat = "USD";
    public const string GoodAsset = "BTC";
    public const string BadAsset = "BAD";

    public const string GoodPaymentMethod = "BTC-OnChain";
    public const string BadPaymentMethod = "BAD-NoChain";

    public KrakenMockHttpMessageHandler()
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage fakeResponse = null;
        if (request.RequestUri != null)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri.ToString().Equals("https://api.kraken.com/0/private/Balance"))
            {
                fakeResponse = new HttpResponseMessage(HttpStatusCode.OK);
                fakeResponse.Content = new StringContent(@"{""error"":[],""result"":{""BCH"":""0.0000000000"",""ZUSD"":""502.1796"",""KFEE"":""0.00"",""XXRP"":""0.00000000"",""XXLM"":""0.00000000"",""XXMR"":""5.0000000100"",""XLTC"":""25.0000066900"",""XXBT"":""0.1250000000"",""XREP"":""0.0000006100"",""XZEC"":""0.0000033800"",""EUR.HOLD"":""0.0000"",""XETH"":""0.0000089300"",""XETC"":""0.0000000300"",""DASH"":""0.0000000000""}}", Encoding.UTF8, "application/json");
            }
            else if (request.Method == HttpMethod.Post && request.RequestUri.ToString().Equals("https://api.kraken.com/0/private/WithdrawStatus"))
            {
                fakeResponse = new HttpResponseMessage(HttpStatusCode.OK);
                fakeResponse.Content =
                    new StringContent(
                        $"{{\"error\":[],\"result\":[{{\"method\":\"Bitcoin\",\"aclass\":\"currency\",\"asset\":\"XXBT\",\"refid\":\"{NewWithdrawalId}\",\"txid\":null,\"info\":\"{TargetWithdrawalAddress}\",\"amount\":\"{WithdrawalAmountExclFee}\",\"fee\":\"{ExpectedWithdrawalFee}\",\"time\":1644000000,\"status\":\"Initial\"}},{{\"method\":\"Bitcoin\",\"aclass\":\"currency\",\"asset\":\"XXBT\",\"refid\":\"YYYYYYY-YYYYYY-YYYYYY\",\"txid\":\"152bdedfe12345678912345678912345678976308119a82d2852360ea28746c0\",\"info\":\"bc1q01234567891234567891234567891234567890\",\"amount\":\"0.14998000\",\"fee\":\"0.00002000\",\"time\":1643820000,\"status\":\"Success\"}},{{\"method\":\"Bitcoin\",\"aclass\":\"currency\",\"asset\":\"XXBT\",\"refid\":\"DDDDDDD-DDDDDD-DDDDDD\",\"txid\":\"1e5fd070848a031123456789373984cf96a8b3419f4d12345678911257ccc6e9\",\"info\":\"bc1q01234567891234567891234567891234567890\",\"amount\":\"0.13643000\",\"fee\":\"0.00002000\",\"time\":1641500000,\"status\":\"Success\"}},{{\"method\":\"Bitcoin\",\"aclass\":\"currency\",\"asset\":\"XXBT\",\"refid\":\"CCCCCCC-CCCCCC-CCCCCC\",\"txid\":\"123456789fc6b4bd16e8dec790db195625410537e2957eeb7b9808b123456789\",\"info\":\"bc1q01234567891234567891234567891234567890\",\"amount\":\"0.11797000\",\"fee\":\"0.00002000\",\"time\":1640000000,\"status\":\"Success\"}},{{\"method\":\"Bitcoin\",\"aclass\":\"currency\",\"asset\":\"XXBT\",\"refid\":\"BBBBBBB-BBBBBB-BBBBBB\",\"txid\":\"88ecc44a8a812345678902007018f25311f7b82237e200777ce7c8a123456789\",\"info\":\"bc1q01234567891234567891234567891234567890\",\"amount\":\"0.11588000\",\"fee\":\"0.00002000\",\"time\":1639000000,\"status\":\"Success\"}},{{\"method\":\"Bitcoin\",\"aclass\":\"currency\",\"asset\":\"XXBT\",\"refid\":\"AAAAAAA-AAAAAA-AAAAAA\",\"txid\":\"123456789be1b8e0067faa4e0db0775b89a3325f7975002da992cbb123456789\",\"info\":\"bc1q01234567891234567891234567891234567890\",\"amount\":\"0.09719000\",\"fee\":\"0.00015000\",\"time\":1637212345,\"status\":\"Success\"}}]}}",
                        Encoding.UTF8, "application/json");
            }
        }

        if (fakeResponse == null)
        {
            throw new NotImplementedException($"Missing mock response for: {request.Method} {request.RequestUri}");
        }

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        tcs.SetResult(fakeResponse);
        return tcs.Task;
    }
}
