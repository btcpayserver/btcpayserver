using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningSupportedPaymentMethod : ISupportedPaymentMethod
    {
        public string CryptoCode { get; set; }
        [Obsolete("Use Get/SetLightningChargeUrl")]
        public string LightningChargeUrl { get; set; }

        public Uri GetLightningChargeUrl(bool withCredentials)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            UriBuilder uri = new UriBuilder(LightningChargeUrl);
            if (withCredentials)
            {
                uri.UserName = Username;
                uri.Password = Password;
            }
#pragma warning restore CS0618 // Type or member is obsolete
            return uri.Uri;
        }

        public void SetLightningChargeUrl(Uri uri)
        {
            if (uri == null)
                throw new ArgumentNullException(nameof(uri));
            if (string.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException(paramName: nameof(uri), message: "Uri should have credential information");
            var splitted = uri.UserInfo.Split(':');
            if (splitted.Length != 2)
                throw new ArgumentException(paramName: nameof(uri), message: "Uri should have credential information");
#pragma warning disable CS0618 // Type or member is obsolete
            Username = splitted[0];
            Password = splitted[1];
            LightningChargeUrl = new UriBuilder(uri) { UserName = "", Password = "" }.Uri.AbsoluteUri;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [Obsolete("Use Get/SetLightningChargeUrl")]
        public string Username { get; set; }
        [Obsolete("Use Get/SetLightningChargeUrl")]
        public string Password { get; set; }
        public PaymentMethodId PaymentId => new PaymentMethodId(CryptoCode, PaymentTypes.LightningLike);
    }
}
