#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
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
    }
}
