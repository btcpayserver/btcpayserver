using System;

namespace BTCPayServer.Client
{
    public class GreenFieldAPIException : Exception
    {
        public GreenFieldAPIException(int httpCode, Models.GreenfieldAPIError error) : base(error.Message)
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
