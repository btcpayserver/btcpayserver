using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Payments.Lightning.Lnd
{
    public abstract class LndAuthentication
    {
        public class FixedMacaroonAuthentication : LndAuthentication
        {
            public FixedMacaroonAuthentication(byte[] macaroon)
            {
                if (macaroon == null)
                    throw new ArgumentNullException(nameof(macaroon));
                Macaroon = macaroon;
            }
            public byte[] Macaroon { get; set; }
            public override void AddAuthentication(HttpRequestMessage httpRequest)
            {
                httpRequest.Headers.Add("Grpc-Metadata-macaroon", Encoders.Hex.EncodeData(Macaroon));
            }
        }
        public class NullAuthentication : LndAuthentication
        {
            public static NullAuthentication Instance { get; } = new NullAuthentication();

            private NullAuthentication()
            {

            }
            public override void AddAuthentication(HttpRequestMessage httpRequest)
            {
            }
        }

        public class MacaroonFileAuthentication : LndAuthentication
        {
            public MacaroonFileAuthentication(string filePath)
            {
                if (filePath == null)
                    throw new ArgumentNullException(nameof(filePath));
                // Because this dump the whole file, let's make sure it is indeed the macaroon
                if (!filePath.EndsWith(".macaroon", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(message: "filePath is not a macaroon file", paramName: nameof(filePath));
                FilePath = filePath;
            }
            public string FilePath { get; set; }
            public override void AddAuthentication(HttpRequestMessage httpRequest)
            {
                try
                {
                    var bytes = File.ReadAllBytes(FilePath);
                    httpRequest.Headers.Add("Grpc-Metadata-macaroon", Encoders.Hex.EncodeData(bytes));
                }
                catch
                {
                }
            }
        }

        public abstract void AddAuthentication(HttpRequestMessage httpRequest);
    }
}
