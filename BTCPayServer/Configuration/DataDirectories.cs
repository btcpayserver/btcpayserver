using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace BTCPayServer.Configuration
{
    public class DataDirectories
    {
        public DataDirectories(IConfiguration conf)
        {
            var networkType = DefaultConfiguration.GetNetworkType(conf);
            var defaultSettings = BTCPayDefaultSettings.GetDefaultSettings(networkType);
            DataDir = conf["datadir"] ?? defaultSettings.DefaultDataDirectory;
            PluginDir = conf["plugindir"] ?? defaultSettings.DefaultPluginDirectory;
            StorageDir = Path.Combine(DataDir, Storage.Services.Providers.FileSystemStorage.FileSystemFileProviderService.LocalStorageDirectoryName);
            TempStorageDir = Path.Combine(StorageDir, "tmp");
        }
        public string DataDir { get; }
        public string PluginDir { get; }
        public string TempStorageDir { get; }
        public string StorageDir { get; set; }
    }
}
