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
            if (string.IsNullOrEmpty(_Options.TorrcFile) || !File.Exists(_Options.TorrcFile))
                return Array.Empty<TorService>();
            List<TorService> result = new List<TorService>();
            try
            {
                var torrcContent = await File.ReadAllTextAsync(_Options.TorrcFile);
                if (!Torrc.TryParse(torrcContent, out var torrc))
                    return Array.Empty<TorService>();

                var services = torrc.ServiceDirectories.SelectMany(d => d.ServicePorts.Select(p => (Directory: new DirectoryInfo(d.DirectoryPath), VirtualPort: p.VirtualPort)))
                .Select(d => (ServiceName: d.Directory.Name, 
                              ReadingLines: System.IO.File.ReadAllLinesAsync(Path.Combine(d.Directory.FullName, "hostname")),
                              VirtualPort: d.VirtualPort))
                .ToArray();
                foreach (var service in services)
                {
                    try
                    {
                        var onionHost = (await service.ReadingLines)[0].Trim();
                        var torService = new TorService()
                        {
                            Name = service.ServiceName,
                            OnionHost = onionHost,
                            VirtualPort = service.VirtualPort
                        };
                        if (service.ServiceName.Equals("BTCPayServer", StringComparison.OrdinalIgnoreCase))
                            torService.ServiceType = TorServiceType.BTCPayServer;
                        result.Add(torService);
                    }
                    catch
                    {

                    }
                }
            }
            catch
            {
            }
            return result.ToArray();
        }
    }

    public class TorService
    {
        public TorServiceType ServiceType { get; set; } = TorServiceType.Other;
        public string Name { get; set; }
        public string OnionHost { get; set; }
        public int VirtualPort { get; set; }
    }

    public enum TorServiceType
    {
        BTCPayServer,
        Other
    }
}
