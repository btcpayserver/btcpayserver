using System.Collections.Generic;
using BTCPayServer.Configuration;

namespace BTCPayServer.Models.ServerViewModels
{
    public class ServicesViewModel
    {
        public class OtherExternalService
        {
            public string Name { get; set; }
            public string Link { get; set; }
        }

        public List<ExternalService> ExternalServices { get; set; } = new ();
        public List<OtherExternalService> OtherExternalServices { get; set; } = new ();
        public List<OtherExternalService> TorHttpServices { get; set; } = new ();
        public List<OtherExternalService> TorOtherServices { get; set; } = new ();
    }
}
