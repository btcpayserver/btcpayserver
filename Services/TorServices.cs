using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Services
{
    public class TorServices : BaseAsyncService
    {
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly IOptions<BTCPayServerOptions> _options;


        public TorServices(BTCPayNetworkProvider btcPayNetworkProvider, IOptions<BTCPayServerOptions> options, Logs logs) : base(logs)
        {
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _options = options;
        }

        public TorService[] Services { get; internal set; } = Array.Empty<TorService>();

        private bool firstRun = true;
        internal async Task Refresh()
        {
            if (firstRun)
            {
                firstRun = false;
            }
            else
            {
                await Task.Delay(TimeSpan.FromSeconds(120), CancellationToken);
            }
            List<TorService> result = new List<TorService>();
            try
            {
                if (!File.Exists(_options.Value.TorrcFile))
                {
                    Logs.PayServer.LogWarning("Torrc file is not found");
                    Services = Array.Empty<TorService>();
                    return;
                }

                var torrcContent = await File.ReadAllTextAsync(_options.Value.TorrcFile);
                if (!Torrc.TryParse(torrcContent, out var torrc))
                {
                    Logs.PayServer.LogWarning("Torrc file could not be parsed");
                    Services = Array.Empty<TorService>();
                    return;
                }

                var torrcDir = Path.GetDirectoryName(_options.Value.TorrcFile);
                var services = torrc.ServiceDirectories.SelectMany(d =>
                        d.ServicePorts.Select(p => (Directory: GetDirectory(d, torrcDir), VirtualPort: p.VirtualPort)))
                    .Select(d => (ServiceName: d.Directory.Name,
                        ReadingLines: System.IO.File.ReadAllLinesAsync(Path.Combine(d.Directory.FullName, "hostname")),
                        VirtualPort: d.VirtualPort))
                    .ToArray();
                foreach (var service in services)
                {
                    try
                    {
                        var onionHost = (await service.ReadingLines)[0].Trim();
                        var torService = ParseService(service.ServiceName, onionHost, service.VirtualPort);
                        result.Add(torService);
                    }
                    catch (Exception ex)
                    {
                        Logs.PayServer.LogWarning(ex,
                            $"Error while reading hidden service {service.ServiceName} configuration");
                    }
                }
            }
            catch (Exception ex)
            {
                Logs.PayServer.LogWarning(ex, $"Error while reading torrc file");
            }

            Services = result.ToArray();
        }

        private TorService ParseService(string serviceName, string onionHost, int virtualPort)
        {
            var torService = new TorService() { Name = serviceName, OnionHost = onionHost, VirtualPort = virtualPort };

            if (Enum.TryParse<TorServiceType>(serviceName, true, out var serviceType))
                torService.ServiceType = serviceType;
            else if (TryParseCryptoSpecificService(serviceName, out var network, out serviceType))
            {
                torService.ServiceType = serviceType;
                torService.Network = network;
            }
            else
            {
                torService.ServiceType = TorServiceType.Other;
            }

            return torService;
        }

        private static DirectoryInfo GetDirectory(HiddenServiceDir hs, string relativeTo)
        {
            if (Path.IsPathRooted(hs.DirectoryPath))
                return new DirectoryInfo(hs.DirectoryPath);
            return new DirectoryInfo(Path.Combine(relativeTo, hs.DirectoryPath));
        }

        private bool TryParseCryptoSpecificService(string name, out BTCPayNetworkBase network,
            out TorServiceType serviceType)
        {
            network = null;
            serviceType = TorServiceType.Other;
            var splitted = name.Trim().Split('-');
            return splitted.Length == 2 && Enum.TryParse(splitted[1], true, out serviceType) &&
                   _btcPayNetworkProvider.TryGetNetwork(splitted[0], out network);
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_options.Value.TorrcFile) && _options.Value.TorServices != null)
            {
                LoadFromConfig();
            }
            else if (!string.IsNullOrEmpty(_options.Value.TorrcFile))
            {
                await Refresh();
                await base.StartAsync(cancellationToken);
            }
        }

        internal override Task[] InitializeTasks()
        {
            return new[] { CreateLoopTask(Refresh) };
        }

        private void LoadFromConfig()
        {
            Services = _options.Value.TorServices.Select(p => p.Split(":", StringSplitOptions.RemoveEmptyEntries))
                .Where(p => p.Length == 3)
                .Select(strings =>
                    int.TryParse(strings[2], out var port) ? ParseService(strings[0], strings[1], port) : null)
                .Where(p => p != null)
                .ToArray();
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
