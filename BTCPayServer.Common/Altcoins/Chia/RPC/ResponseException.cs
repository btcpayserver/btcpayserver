using System;

namespace BTCPayServer.Common.Altcoins.Chia.RPC
{
    /// <summary>
    /// Exception thrown when the RPC endpoint returns a response <see cref="Message"/> but Data.success is false
    /// oro there is a communication error on the websocket of http channgel
    /// </summary>
    public sealed class ResponseException : Exception
    {
        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="request">The request sent to the service</param>
        public ResponseException(Message request)
            : this(request, "The RPC endpoint returned success == false", null)
        {
        }

        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="request">The request sent to the service</param>
        /// <param name="message"><see cref="Exception.Message"/></param>
        public ResponseException(Message request, string message)
            : this(request, message, null)
        {
        }

        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="request">The request sent to the service</param>
        /// <param name="message"><see cref="Exception.Message"/></param>
        /// <param name="innerException"><see cref="Exception.InnerException"/></param>
        public ResponseException(Message request, string message, Exception? innerException)
            : base(message, innerException)
        {
            Request = request;
        }

        /// <summary>
        /// The request sent to the service
        /// </summary>
        public Message Request { get; init; }
    }
}
