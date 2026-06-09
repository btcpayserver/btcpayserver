#nullable enable
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

            var name = user.GetBlob()?.Name;
            try
            {
                return new MimeKit.MailboxAddress(name ?? "", user.Email!);
            }
            catch  // Invalid encoding or format; treat as no valid mailbox
            {
            }
            return new MimeKit.MailboxAddress(string.Empty, user.Email!);
        }
    }
}
