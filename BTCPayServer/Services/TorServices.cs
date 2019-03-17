using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Configuration;

namespace BTCPayServer.Services
{
    public class TorServices
    {
        BTCPayServerOptions _Options;
        public TorServices(BTCPayServerOptions options)
        {
            _Options = options;
        }

        public async Task<TorService[]> GetServices()
        {
            if (string.IsNullOrEmpty(_Options.TorHiddenServicesDirectory) || !Directory.Exists(_Options.TorHiddenServicesDirectory))
                return Array.Empty<TorService>();
            List<TorService> result = new List<TorService>();
            var servicesDirs = Directory.GetDirectories(_Options.TorHiddenServicesDirectory);
            var services = servicesDirs
                .Select(d => new DirectoryInfo(d))
                .Select(d => (ServiceName: d.Name, ReadingLines: System.IO.File.ReadAllLinesAsync(Path.Combine(d.FullName, "hostname"))))
                .ToArray();
            foreach (var service in services)
            {
                try
                {
                    var onionHost = (await service.ReadingLines)[0].Trim();
                    var torService = new TorService() { Name = service.ServiceName, OnionHost = onionHost };
                    if (service.ServiceName.Equals("BTCPayServer", StringComparison.OrdinalIgnoreCase))
                        torService.ServiceType = TorServiceType.BTCPayServer;
                    result.Add(torService);
                }
                catch
                {

                }
            }
            return result.ToArray();
        }
    }

    public class TorService
    {
        public TorServiceType ServiceType { get; set; } = TorServiceType.Other;
        public string Name { get; set; }
        public string OnionHost { get; set; }
    }

    public enum TorServiceType
    {
        BTCPayServer,
        Other
    }
}
