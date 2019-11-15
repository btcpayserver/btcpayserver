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
        private readonly BTCPayNetworkProvider _networks;
        BTCPayServerOptions _Options;
        public TorServices(BTCPayServer.BTCPayNetworkProvider networks, BTCPayServerOptions options)
        {
            _networks = networks;
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
                var torrcDir = Path.GetDirectoryName(_Options.TorrcFile);
                var services = torrc.ServiceDirectories.SelectMany(d => d.ServicePorts.Select(p => (Directory: GetDirectory(d, torrcDir), VirtualPort: p.VirtualPort)))
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
                        else if (TryParseP2PService(service.ServiceName, out var network, out var serviceType))
                        {
                            torService.ServiceType = serviceType;
                            torService.Network = network;
                        }
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

        private static DirectoryInfo GetDirectory(HiddenServiceDir hs, string relativeTo)
        {
            if (Path.IsPathRooted(hs.DirectoryPath))
                return new DirectoryInfo(hs.DirectoryPath);
            return new DirectoryInfo(Path.Combine(relativeTo, hs.DirectoryPath));
        }

        private bool TryParseP2PService(string name, out BTCPayNetworkBase network, out TorServiceType serviceType)
        {
            network = null;
            serviceType = TorServiceType.Other;
            var splitted = name.Trim().Split('-');
            if (splitted.Length == 2 && splitted[1] == "P2P")
            {
                serviceType = TorServiceType.P2P;
            }
            else if (splitted.Length == 2 && splitted[1] == "RPC")
            {
                serviceType = TorServiceType.RPC;
            }
            else
            {
                return false;
            }
            network = _networks.GetNetwork<BTCPayNetworkBase>(splitted[0]);
            return network != null;
        }
    }

    public class TorService
    {
        public TorServiceType ServiceType { get; set; } = TorServiceType.Other;
        public BTCPayNetworkBase Network { get; set; }
        public string Name { get; set; }
        public string OnionHost { get; set; }
        public int VirtualPort { get; set; }
    }

    public enum TorServiceType
    {
        BTCPayServer,
        P2P,
        RPC,
        Other
    }
}
