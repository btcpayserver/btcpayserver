using System.Linq;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json.Linq;

namespace BTCPayServer
{
    public static class UserExtensions
    {
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
