using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using NBitcoin;

namespace BTCPayServer.Configuration
{
    public class ExternalServicesOptions
    {
        public Dictionary<string, Uri> OtherExternalServices { get; set; } = new Dictionary<string, Uri>();
        public ExternalServices ExternalServices { get; set; } = new ExternalServices();

        public void Configure(IConfiguration configuration, BTCPayNetworkProvider btcPayNetworkProvider)
        {
            foreach (var net in btcPayNetworkProvider.GetAll().OfType<BTCPayNetwork>())
            {
                ExternalServices.Load(net.CryptoCode, configuration);
            }

            ExternalServices.LoadNonCryptoServices(configuration);

            var services = configuration.GetOrDefault<string>("externalservices", null);
            if (services != null)
            {
                foreach (var service in services.Split(new[] {';', ','}, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => (p, SeparatorIndex: p.IndexOf(':', StringComparison.OrdinalIgnoreCase)))
                    .Where(p => p.SeparatorIndex != -1)
                    .Select(p => (Name: p.p.Substring(0, p.SeparatorIndex),
                        Link: p.p.Substring(p.SeparatorIndex + 1))))
                {
                    if (Uri.TryCreate(service.Link, UriKind.RelativeOrAbsolute, out var uri))
                        OtherExternalServices.AddOrReplace(service.Name, uri);
                }
            }
        }
    }
}
