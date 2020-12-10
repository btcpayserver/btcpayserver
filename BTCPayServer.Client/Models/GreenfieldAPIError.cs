using System;

namespace BTCPayServer.Client.Models
{
    public class GreenfieldAPIError
    {
        public GreenfieldAPIError()
        {

        }
        public GreenfieldAPIError(string code, string message)
        {
            code = code ?? "generic-error";
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            Code = code;
            Message = message;
        }
        public string Code { get; set; }
        public string Message { get; set; }
    }
}
