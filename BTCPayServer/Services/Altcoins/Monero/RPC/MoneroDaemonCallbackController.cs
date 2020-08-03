#if ALTCOINS
using BTCPayServer.Filters;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Services.Altcoins.Monero.RPC
{
    [Route("[controller]")]
    [OnlyIfSupportAttribute("XMR")]
    public class MoneroLikeDaemonCallbackController : Controller
    {
        private readonly EventAggregator _eventAggregator;

        public MoneroLikeDaemonCallbackController(EventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }
        [HttpGet("block")]
        public IActionResult OnBlockNotify(string hash, string cryptoCode)
        {
            _eventAggregator.Publish(new MoneroEvent()
            {
                BlockHash = hash,
                CryptoCode = cryptoCode.ToUpperInvariant()
            });
            return Ok();
        }
        [HttpGet("tx")]
        public IActionResult OnTransactionNotify(string hash, string cryptoCode)
        {
            _eventAggregator.Publish(new MoneroEvent()
            {
                TransactionHash = hash,
                CryptoCode = cryptoCode.ToUpperInvariant()
            });
            return Ok();
        }

    }
}
#endif
