using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BTCPayServer
{
    class ZipUtils
    {
        public static byte[] Zip(string unzipped)
        {
            MemoryStream ms = new MemoryStream();
            using (GZipStream gzip = new GZipStream(ms, CompressionMode.Compress))
            {
                StreamWriter writer = new StreamWriter(gzip, Encoding.UTF8);
                writer.Write(unzipped);
                writer.Flush();
            }
            return ms.ToArray();
        }

        public static string Unzip(byte[] bytes)
        {
            MemoryStream ms = new MemoryStream(bytes);
            using (GZipStream gzip = new GZipStream(ms, CompressionMode.Decompress))
            {
                StreamReader reader = new StreamReader(gzip, Encoding.UTF8);
                var unzipped = reader.ReadToEnd();
                return unzipped;
            }
        }
    }
}
