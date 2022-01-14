using System;

namespace BTCPayServer.Client
{
    public class GreenfieldAPIException : Exception
    {
        public GreenfieldAPIException(int httpCode, Models.GreenfieldAPIError error) : base(error.Message)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));
            HttpCode = httpCode;
            APIError = error;
        }
        public Models.GreenfieldAPIError APIError { get; }
        public int HttpCode { get; set; }
    }
}
