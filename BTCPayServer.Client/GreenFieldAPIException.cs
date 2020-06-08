using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;

namespace BTCPayServer.Client
{
    public class GreenFieldAPIException : Exception
    {
        public GreenFieldAPIException(Models.GreenfieldAPIError error):base(error.Message)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));
            APIError = error;
        }
        public Models.GreenfieldAPIError APIError { get; }
    }
}
