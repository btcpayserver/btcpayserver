using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Payments
{
    /// <summary>
    /// The different ways to pay an invoice
    /// </summary>
    public enum PaymentTypes
    {
        /// <summary>
        /// On-Chain UTXO based, bitcoin compatible
        /// </summary>
        BTCLike,
        /// <summary>
        /// Lightning payment
        /// </summary>
        LightningLike
    }
}
