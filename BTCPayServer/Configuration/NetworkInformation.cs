using Microsoft.Extensions.Configuration;
using NBitcoin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Configuration
{
    public class NetworkInformation
    {
        static NetworkInformation()
        {
            _Networks = new Dictionary<string, NetworkInformation>();
            foreach (var network in new[] { Network.Main, Network.TestNet, Network.RegTest })
            {
                NetworkInformation info = new NetworkInformation();
                info.DefaultDataDirectory = StandardConfiguration.DefaultDataDirectory.GetDirectory("BTCPayServer", network.Name);
                info.DefaultConfigurationFile = Path.Combine(info.DefaultDataDirectory, "settings.config");
                info.Network = network;
                info.DefaultPort = 23002;
                _Networks.Add(network.Name, info);
                if (network == Network.Main)
                {
                    info.DefaultPort = 23000;
                }
                if (network == Network.TestNet)
                {
                    info.DefaultPort = 23001;
                }
            }
        }

        static Dictionary<string, NetworkInformation> _Networks;
        public static NetworkInformation GetNetworkByName(string name)
        {
            var value = _Networks.TryGet(name);
            if (value != null)
                return value;

            //Maybe alias ?
            var network = Network.GetNetwork(name);
            if (network != null)
            {
                value = _Networks.TryGet(network.Name);
                if (value != null)
                    return value;
            }
            return null;
        }
        
        public Network Network
        {
            get; set;
        }
        public string DefaultConfigurationFile
        {
            get;
            set;
        }
        public string DefaultDataDirectory
        {
            get;
            set;
        }
        public int DefaultPort
        {
            get;
            private set;
        }

        public override string ToString()
        {
            return Network.ToString();
        }

        public static string ToStringAll()
        {
            return string.Join(", ", _Networks.Select(n => n.Key).ToArray());
        }
    }
}
