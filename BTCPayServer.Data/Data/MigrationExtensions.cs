using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BTCPayServer.Data
{
    public static class MigrationExtensions
    {
        public static string Unzip(byte[] bytes)
        {
            MemoryStream ms = new MemoryStream(bytes);
            using GZipStream gzip = new GZipStream(ms, CompressionMode.Decompress);
            StreamReader reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        // Make postgres happy
        public static string SanitizeJSON(string json) => json.Replace("\\u0000", string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
