using Fido2NetLib;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Fido2.Models
{
    public class LoginWithFido2ViewModel
    {
        public string UserId { get; set; }

        public bool RememberMe { get; set; }
        public string Data { get; set; }
        public string Response { get; set; }
    }
}
