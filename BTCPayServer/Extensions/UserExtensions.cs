using System;
using BTCPayServer.Data;

namespace BTCPayServer
{
    public static class UserExtensions
    {
        public static MimeKit.MailboxAddress GetMailboxAddress(this ApplicationUser user)
        {
            if (user is null)
                throw new ArgumentNullException(nameof(user));
            var name = user.UserName ?? String.Empty;
            if (user.Email == user.UserName)
                name = String.Empty;
            return new MimeKit.MailboxAddress(name, user.Email);
        }
    }
}
