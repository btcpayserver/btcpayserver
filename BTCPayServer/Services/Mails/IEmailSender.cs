using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Mails
{
    public interface IEmailSender
    {
        void SendEmail(string email, string subject, string message);
    }
}
