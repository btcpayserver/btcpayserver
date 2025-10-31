using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning;

namespace BTCPayServer.Lightning.LNbits
{
    public class LNbitsLightningClient : ILightningClient
    {
        private readonly HttpClient _httpClient;
        private readonly Uri _baseUri;
        private readonly string _apiKey;

        public LNbitsLightningClient(Uri baseUri, string walletId, string apiKey)
        {
            _baseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            
            _httpClient = new HttpClient { BaseAddress = baseUri };
            _httpClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation = default)
        {
            var amountSats = (long)amount.ToUnit(LightMoneyUnit.Satoshi);
            var requestData = new { @out = false, amount = amountSats, memo = description ?? "BTCPay Server Invoice", unit = "sat", expiry = (int)expiry.TotalSeconds };
            var response = await _httpClient.PostAsJsonAsync("/api/v1/payments", requestData, cancellation);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<LNbitsInvoiceResponse>(cancellationToken: cancellation);
            return new LightningInvoice { Id = result.payment_hash, PaymentHash = result.payment_hash, BOLT11 = result.payment_request, Status = LightningInvoiceStatus.Unpaid, Amount = amount, ExpiresAt = DateTimeOffset.UtcNow.Add(expiry) };
        }

        public async Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default)
        {
            var response = await _httpClient.GetAsync($"/api/v1/payments/{invoiceId}", cancellation);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<LNbitsPaymentResponse>(cancellationToken: cancellation);
            var status = result.paid ? LightningInvoiceStatus.Paid : LightningInvoiceStatus.Unpaid;
            var amount = LightMoney.MilliSatoshis(result.amount);
            return new LightningInvoice { Id = result.payment_hash, PaymentHash = result.payment_hash, BOLT11 = result.bolt11, Status = status, Amount = amount, PaidAt = result.paid && result.time > 0 ? DateTimeOffset.FromUnixTimeSeconds(result.time) : null };
        }

        public async Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default)
        {
            var response = await _httpClient.GetAsync("/api/v1/wallet", cancellation);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<LNbitsWalletInfo>(cancellationToken: cancellation);
            return new LightningNodeInformation { NodeInfoList = new[] { new NodeInfo { NodeId = "lnbits", Host = _baseUri.Host, Port = _baseUri.Port } } };
        }

        public Task<LightningPayment> Pay(string bolt11, PayInvoiceParams payParams = null, CancellationToken cancellation = default) { throw new NotImplementedException(); }
        public Task<LightningPayment> GetPayment(string paymentHash, CancellationToken cancellation = default) { throw new NotImplementedException(); }
        public Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = default) { throw new NotImplementedException(); }
        public Task<LightningPayment[]> ListPayments(CancellationToken cancellation = default) { throw new NotImplementedException(); }
        public Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default) { return Task.FromResult(Array.Empty<LightningChannel>()); }
        public Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest, CancellationToken cancellation = default) { throw new NotSupportedException(); }
        public Task ConnectTo(NodeInfo nodeInfo, CancellationToken cancellation = default) { throw new NotSupportedException(); }
        public Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = default) { throw new NotSupportedException(); }
        public void Dispose() { _httpClient?.Dispose(); }
    }

    internal class LNbitsInvoiceResponse { public string payment_hash { get; set; } public string payment_request { get; set; } public string checking_id { get; set; } }
    internal class LNbitsPaymentResponse { public string payment_hash { get; set; } public bool paid { get; set; } public long amount { get; set; } public string bolt11 { get; set; } public long time { get; set; } public string memo { get; set; } }
    internal class LNbitsWalletInfo { public string id { get; set; } public string name { get; set; } public long balance { get; set; } }
}
