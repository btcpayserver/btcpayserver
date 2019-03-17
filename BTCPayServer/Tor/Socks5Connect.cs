using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Tor
{
    public enum SocksErrorCode
    {
        Success = 0,
        GeneralServerFailure = 1,
        ConnectionNotAllowed = 2,
        NetworkUnreachable = 3,
        HostUnreachable = 4,
        ConnectionRefused = 5,
        TTLExpired = 6,
        CommandNotSupported = 7,
        AddressTypeNotSupported = 8,
    }
    public class SocksException : Exception
    {
        public SocksException(SocksErrorCode errorCode) : base(GetMessageForCode((int)errorCode))
        {
            SocksErrorCode = errorCode;
        }

        public SocksErrorCode SocksErrorCode
        {
            get; set;
        }

        private static string GetMessageForCode(int errorCode)
        {
            switch (errorCode)
            {
                case 0:
                    return "Success";
                case 1:
                    return "general SOCKS server failure";
                case 2:
                    return "connection not allowed by ruleset";
                case 3:
                    return "Network unreachable";
                case 4:
                    return "Host unreachable";
                case 5:
                    return "Connection refused";
                case 6:
                    return "TTL expired";
                case 7:
                    return "Command not supported";
                case 8:
                    return "Address type not supported";
                default:
                    return "Unknown code";
            }
        }

        public SocksException(string message) : base(message)
        {

        }
    }

    public class Socks5Connect
    {
        static readonly byte[] SelectionMessage = new byte[] { 5, 1, 0 };
        public static async Task<Socket> ConnectSocksAsync(EndPoint socksEndpoint, DnsEndPoint endpoint, CancellationToken cancellation)
        {
            Socket s = null;
            int maxTries = 3;
            int retry = 0;
            try
            {
                while (true)
                {
                    s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    await s.ConnectAsync(socksEndpoint).WithCancellation(cancellation).ConfigureAwait(false);
                    NetworkStream stream = new NetworkStream(s, false);

                    await stream.WriteAsync(SelectionMessage, 0, SelectionMessage.Length, cancellation).ConfigureAwait(false);
                    await stream.FlushAsync(cancellation).ConfigureAwait(false);

                    var selectionResponse = new byte[2];
                    await stream.ReadAsync(selectionResponse, 0, 2, cancellation);
                    if (selectionResponse[0] != 5)
                        throw new SocksException("Invalid version in selection reply");
                    if (selectionResponse[1] != 0)
                        throw new SocksException("Unsupported authentication method in selection reply");

                    var connectBytes = CreateConnectMessage(endpoint.Host, endpoint.Port);
                    await stream.WriteAsync(connectBytes, 0, connectBytes.Length, cancellation).ConfigureAwait(false);
                    await stream.FlushAsync(cancellation).ConfigureAwait(false);

                    var connectResponse = new byte[10];
                    await stream.ReadAsync(connectResponse, 0, 10, cancellation);
                    if (connectResponse[0] != 5)
                        throw new SocksException("Invalid version in connect reply");
                    if (connectResponse[1] != 0)
                    {
                        var code = (SocksErrorCode)connectResponse[1];
                        if (!IsTransient(code) || retry++ >= maxTries)
                            throw new SocksException(code);
                        CloseSocket(ref s);
                        await Task.Delay(1000, cancellation).ConfigureAwait(false);
                        continue;
                    }
                    if (connectResponse[2] != 0)
                        throw new SocksException("Invalid RSV in connect reply");
                    if (connectResponse[3] != 1)
                        throw new SocksException("Invalid ATYP in connect reply");
                    for (int i = 4; i < 4 + 4; i++)
                    {
                        if (connectResponse[i] != 0)
                            throw new SocksException("Invalid BIND address in connect reply");
                    }

                    if (connectResponse[8] != 0 || connectResponse[9] != 0)
                        throw new SocksException("Invalid PORT address connect reply");
                    return s;
                }
            }
            catch
            {
                CloseSocket(ref s);
                throw;
            }
        }

        private static void CloseSocket(ref Socket s)
        {
            if (s == null)
                return;
            try
            {
                s.Shutdown(SocketShutdown.Both);
            }
            catch
            {
                try
                {
                    s.Dispose();
                }
                catch { }
            }
            finally
            {
                s = null;
            }
        }

        private static bool IsTransient(SocksErrorCode code)
        {
            return code == SocksErrorCode.GeneralServerFailure ||
                   code == SocksErrorCode.TTLExpired;
        }

        internal static byte[] CreateConnectMessage(string host, int port)
        {
            byte[] sendBuffer;
            byte[] nameBytes = Encoding.ASCII.GetBytes(host);

            var addressBytes =
                Enumerable.Empty<byte>()
                .Concat(new[] { (byte)nameBytes.Length })
                .Concat(nameBytes).ToArray();

            sendBuffer =
                    Enumerable.Empty<byte>()
                    .Concat(
                        new byte[]
                        {
                            (byte)5, (byte) 0x01, (byte) 0x00, (byte)0x03
                        })
                        .Concat(addressBytes)
                        .Concat(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)port))).ToArray();
            return sendBuffer;
        }
    }
}
