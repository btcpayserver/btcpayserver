using System;
using System.Linq;
using BTCPayServer.Altcoins.Monero.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Altcoins.Monero.RPC
{
    [Route("[controller]")]
    public class MoneroLikeDaemonCallbackController : Controller
    {
        private readonly EventAggregator _eventAggregator;
        private readonly MoneroLikeConfiguration _MoneroLikeConfiguration;

        public MoneroLikeDaemonCallbackController(EventAggregator eventAggregator,
            MoneroLikeConfiguration moneroLikeConfiguration)
        {
            _eventAggregator = eventAggregator;
            _MoneroLikeConfiguration = moneroLikeConfiguration;
        }

        [HttpGet("block")]
        public IActionResult OnBlockNotify(string hash, string cryptoCode)
        {
            var result = Guard(cryptoCode, hash);
            if (result != null) return result;
            _eventAggregator.Publish(new MoneroEvent() {BlockHash = hash, CryptoCode = cryptoCode.ToUpperInvariant()});
            return Ok();
        }

        [HttpGet("tx")]
        public IActionResult OnTransactionNotify(string hash, string cryptoCode)
        {
            var result = Guard(cryptoCode, hash);
            if (result != null) return result;
            _eventAggregator.Publish(new MoneroEvent()
            {
                TransactionHash = hash, CryptoCode = cryptoCode.ToUpperInvariant()
            });
            return Ok();
        }

        private IActionResult Guard(string cryptoCode, string hash)
        {
            if (string.IsNullOrEmpty(cryptoCode) || string.IsNullOrEmpty(hash) ||
                !_MoneroLikeConfiguration.MoneroLikeConfigurationItems.Any(pair =>
                    pair.Key.Equals(cryptoCode, StringComparison.InvariantCultureIgnoreCase)))
            {
                return NotFound();
            }

            return null;
        }
    }
}
