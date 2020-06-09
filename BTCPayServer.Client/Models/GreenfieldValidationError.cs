using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Client.Models
{
    public class GreenfieldValidationError
    {
        public GreenfieldValidationError()
        {

        }
        public GreenfieldValidationError(string path, string message)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            Path = path;
            Message = message;
        }

        public string Path { get; set; }
        public string Message { get; set; }
    }
}
