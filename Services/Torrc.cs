using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace BTCPayServer.Services
{
    public class Torrc
    {
        public static bool TryParse(string str, out Torrc value)
        {
            value = null;
            List<HiddenServiceDir> serviceDirectories = new List<HiddenServiceDir>();
            var lines = str.Split(new char[] { '\n' });
            HiddenServiceDir currentDirectory = null;
            foreach (var line in lines)
            {
                if (HiddenServiceDir.TryParse(line, out var dir))
                {
                    serviceDirectories.Add(dir);
                    currentDirectory = dir;
                }
                else if (HiddenServicePortDefinition.TryParse(line, out var portDef) && currentDirectory != null)
                {
                    currentDirectory.ServicePorts.Add(portDef);
                }
            }
            value = new Torrc() { ServiceDirectories = serviceDirectories };
            return true;
        }

        public List<HiddenServiceDir> ServiceDirectories { get; set; } = new List<HiddenServiceDir>();

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            foreach (var serviceDir in ServiceDirectories)
            {
                builder.AppendLine(serviceDir.ToString());
                foreach (var port in serviceDir.ServicePorts)
                    builder.AppendLine(port.ToString());
            }
            return builder.ToString();
        }
    }

    public class HiddenServiceDir
    {
        public static bool TryParse(string str, out HiddenServiceDir serviceDir)
        {
            serviceDir = null;
            if (!str.Trim().StartsWith("HiddenServiceDir ", StringComparison.OrdinalIgnoreCase))
                return false;
            var parts = str.Split(new char[] { ' ', '\t' }, StringSplitOptions.None);
            if (parts.Length != 2)
                return false;
            serviceDir = new HiddenServiceDir() { DirectoryPath = parts[1].Trim() };
            return true;
        }

        public string DirectoryPath { get; set; }
        public List<HiddenServicePortDefinition> ServicePorts { get; set; } = new List<HiddenServicePortDefinition>();

        public override string ToString()
        {
            return $"HiddenServiceDir {DirectoryPath}";
        }
    }
    public class HiddenServicePortDefinition
    {
        public static bool TryParse(string str, out HiddenServicePortDefinition portDefinition)
        {
            portDefinition = null;
            if (!str.Trim().StartsWith("HiddenServicePort ", StringComparison.OrdinalIgnoreCase))
                return false;
            var parts = str.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
                return false;
            if (!int.TryParse(parts[1].Trim(), out int virtualPort))
                return false;
            var addressPort = parts[2].Trim().Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (addressPort.Length != 2)
                return false;
            if (!int.TryParse(addressPort[1].Trim(), out int port))
                return false;
            if (!IPAddress.TryParse(addressPort[0].Trim(), out IPAddress address))
                return false;
            portDefinition = new HiddenServicePortDefinition() { VirtualPort = virtualPort, Endpoint = new IPEndPoint(address, port) };
            return true;
        }
        public int VirtualPort { get; set; }
        public IPEndPoint Endpoint { get; set; }
        public override string ToString()
        {
            return $"HiddenServicePort {VirtualPort} {Endpoint}";
        }
    }
}
