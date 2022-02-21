using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Custodian.Client.Kraken;

public class KrakenMockHttpMessageHandler : HttpMessageHandler
{
    public KrakenMockHttpMessageHandler()
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage fakeResponse = new HttpResponseMessage(HttpStatusCode.BadRequest);
        if (request.RequestUri != null)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri.ToString().Equals("https://api.kraken.com/0/private/Balance"))
            {
                fakeResponse = new HttpResponseMessage(HttpStatusCode.OK);
                fakeResponse.Content = new StringContent(@"{""error"":[],""result"":{""BCH"":""0.0000000000"",""ZEUR"":""502.1796"",""KFEE"":""0.00"",""XXRP"":""0.00000000"",""XXLM"":""0.00000000"",""XXMR"":""5.0000000100"",""XLTC"":""25.0000066900"",""XXBT"":""0.1250000000"",""XREP"":""0.0000006100"",""XZEC"":""0.0000033800"",""EUR.HOLD"":""0.0000"",""XETH"":""0.0000089300"",""XETC"":""0.0000000300"",""DASH"":""0.0000000000""}}", Encoding.UTF8, "application/json");
            }
        }

        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        tcs.SetResult(fakeResponse);
        return tcs.Task;
    }
}
