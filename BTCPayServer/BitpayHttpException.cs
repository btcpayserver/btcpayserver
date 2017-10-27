using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer
{
    public class BitpayHttpException : Exception
    {
        public BitpayHttpException(int code, string message) : base(message)
        {
            StatusCode = code;
        }
        public int StatusCode
        {
            get; set;
        }
    }
}
