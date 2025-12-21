using System;

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
