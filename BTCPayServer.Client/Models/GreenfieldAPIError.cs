using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Client.Models
{
    public class GreenfieldAPIError
    {
        public GreenfieldAPIError()
        {

        }
        public GreenfieldAPIError(string code, string message)
        {
            if (code == null)
                throw new ArgumentNullException(nameof(code));
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            Code = code;
            Message = message;
        }
        public string Code { get; set; }
        public string Message { get; set; }
    }
}
