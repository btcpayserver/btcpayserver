using System;
using System.Linq;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json.Linq;

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
        public static UserBlob GetBlob(this ApplicationUser user)
        {
            var result = user.Blob == null
                ? new UserBlob()
                : JObject.Parse(ZipUtils.Unzip(user.Blob)).ToObject<UserBlob>();
            return result;
        }
        public static bool SetBlob(this ApplicationUser user, UserBlob blob)
        {
            var newBytes = InvoiceRepository.ToBytes(blob);
            if (user.Blob != null && newBytes.SequenceEqual(user.Blob))
            {
                return false;
            }
            user.Blob = newBytes;
            return true;
        }
    }

    public class UserBlob
    {
        public bool ShowInvoiceStatusChangeHint { get; set; }
    }
}
