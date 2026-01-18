#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning;

namespace BTCPayServer.Payments.Lightning
{
    public interface IExtendedLightningClient : ILightningClient
    {
        /// <summary>
        /// Used to validate the client configuration
        /// </summary>
        /// <returns></returns>
        public Task<ValidationResult?> Validate();
        /// <summary>
        /// The display name of this client (ie. LND (REST), Eclair, LNDhub)
        /// </summary>
        public string? DisplayName { get; }
        /// <summary>
        /// The server URI of this client (ie. http://localhost:8080)
        /// </summary>
        public Uri? ServerUri { get; }
        /// <summary>
        /// Returns the minimum and maximum invoice expiry limits supported by this lightning implementation.
        /// Some implementations (e.g. Boltz) have restrictions around the invoice expiry they can produce.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The expiry limits, or null if no limits apply.</returns>
        public Task<ExpiryLimits?> GetExpiryLimits(CancellationToken cancellationToken = default) => Task.FromResult<ExpiryLimits?>(null);
    }
}
