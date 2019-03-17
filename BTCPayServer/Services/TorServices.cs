using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Logging;

namespace BTCPayServer.Services
{
    public class TorServices
    {
        BTCPayServerOptions _Options;
        public TorServices(BTCPayServerOptions options)
        {
            _Options = options;
        }

        public TorService[] Services { get; internal set; } = Array.Empty<TorService>();


        internal async Task Refresh()
        {
            if (string.IsNullOrEmpty(_Options.TorrcFile) || !File.Exists(_Options.TorrcFile))
            {
                if (!string.IsNullOrEmpty(_Options.TorrcFile))
                    Logs.PayServer.LogWarning("Torrc file is not found");
                Services = Array.Empty<TorService>();
                return;
            }
            List<TorService> result = new List<TorService>();
            try
            {
                var torrcContent = await File.ReadAllTextAsync(_Options.TorrcFile);
                if (!Torrc.TryParse(torrcContent, out var torrc))
                {
                    Logs.PayServer.LogWarning("Torrc file could not be parsed");
                    Services = Array.Empty<TorService>();
                    return;
                }

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
                    catch (Exception ex)
                    {
                        Logs.PayServer.LogWarning(ex, $"Error while reading hidden service {service.ServiceName} configuration");
                    }
                }
            }
            catch (Exception ex)
            {
                Logs.PayServer.LogWarning(ex, $"Error while reading torrc file");
            }
            Services = result.ToArray();
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
