using System.IO;

namespace BTCPayServer.Configuration
{
    public class DataDirectories
    {
        public string DataDir { get; set; }
        public string PluginDir { get; set; }
        public string TempStorageDir { get; set; }
        public string StorageDir { get; set; }
        public string TempDir { get; set; }

        public string ToDatadirFullPath(string path)
        {
            if (Path.IsPathRooted(path))
                return path;
            return Path.Combine(DataDir, path);
        }
    }
}
