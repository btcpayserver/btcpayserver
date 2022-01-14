using System;

namespace BTCPayServer.Client
{
    public class GreenfieldAPIException : Exception
    {
        public GreenfieldAPIException(int httpCode, Models.GreenfieldAPIError error) : base(error.Message)
        {
            HttpCode = httpCode;
            APIError = error ?? throw new ArgumentNullException(nameof(error));
        }
        public Models.GreenfieldAPIError APIError { get; }
        public int HttpCode { get; set; }
    }
}
