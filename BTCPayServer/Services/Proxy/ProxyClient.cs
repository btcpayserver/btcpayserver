using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Proxy
{
    /// <summary>
    /// https://github.com/TheSuunny/Yove.Proxy
    /// </summary>
    public class ProxyClient : IDisposable, IWebProxy
    {
        public enum ProxyType
        {
            Socks4,
            Socks5
        }
        #region IWebProxy

        public ICredentials Credentials { get; set; }

        public int ReadWriteTimeOut { get; set; } = 60000;

        public Uri GetProxy(Uri destination) => HttpProxyURL;
        public bool IsBypassed(Uri host) => false;

        #endregion

        #region ProxyClient

        private Uri HttpProxyURL { get; set; }
        private Socket InternalSocketServer { get; set; }
        private int InternalSocketPort { get; set; }

        private IPAddress Host { get; set; }
        private int Port { get; set; }
        private ProxyType Type { get; set; }
        private int SocksVersion { get; set; }

        public bool IsDisposed { get; set; }

        #endregion

        #region Constants

        private const byte AddressTypeIPV4 = 0x01;
        private const byte AddressTypeIPV6 = 0x04;
        private const byte AddressTypeDomainName = 0x03;

        #endregion

        public ProxyClient(string Proxy, ProxyType Type)
        {
            string Host = Proxy.Split(':')[0]?.Trim();
            int Port = Convert.ToInt32(Proxy.Split(':')[1]?.Trim());

            if (string.IsNullOrEmpty(Host))
                throw new ArgumentNullException("Host null or empty");

            if (Port < 0 || Port > 65535)
                throw new ArgumentOutOfRangeException("Port goes beyond");

            this.Host = GetHost(Host);
            this.Port = Port;
            this.Type = Type;

            SocksVersion = (Type == ProxyType.Socks4) ? 4 : 5;

            CreateInternalServer();
        }

        public ProxyClient(string Host, int Port, ProxyType Type)
        {
            if (string.IsNullOrEmpty(Host))
                throw new ArgumentNullException("Host null or empty");

            if (Port < 0 || Port > 65535)
                throw new ArgumentOutOfRangeException("Port goes beyond");

            this.Host = GetHost(Host);
            this.Port = Port;
            this.Type = Type;

            SocksVersion = (Type == ProxyType.Socks4) ? 4 : 5;

            CreateInternalServer();
        }

        private void CreateInternalServer()
        {
            InternalSocketServer = CreateSocketServer();

            InternalSocketServer.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            InternalSocketPort = ((IPEndPoint)(InternalSocketServer.LocalEndPoint)).Port;

            HttpProxyURL = new Uri($"http://127.0.0.1:{InternalSocketPort}");

            InternalSocketServer.Listen(512);
            InternalSocketServer.BeginAccept(new AsyncCallback(AcceptCallback), null);
        }

        private async void AcceptCallback(IAsyncResult e)
        {
            if (IsDisposed)
                return;

            Socket Socket = InternalSocketServer.EndAccept(e);
            InternalSocketServer.BeginAccept(new AsyncCallback(AcceptCallback), null);

            byte[] HeaderBuffer = new byte[8192]; // Default Header size

            Socket.Receive(HeaderBuffer, HeaderBuffer.Length, 0);

            string Header = Encoding.ASCII.GetString(HeaderBuffer);

            string HttpVersion = Header.Split(' ')[2].Split('\r')[0]?.Trim();
            string TargetURL = Header.Split(' ')[1]?.Trim();

            if (string.IsNullOrEmpty(HttpVersion) || string.IsNullOrEmpty(TargetURL))
                throw new Exception("Unsupported request.");

            string UriHostname = string.Empty;
            int UriPort = 0;

            if (TargetURL.Contains(":") && !TargetURL.Contains("http://"))
            {
                UriHostname = TargetURL.Split(':')[0];
                UriPort = int.Parse(TargetURL.Split(':')[1]);
            }
            else
            {
                Uri URL = new Uri(TargetURL);

                UriHostname = URL.Host;
                UriPort = URL.Port;
            }

            Socket TargetSocket = CreateSocketServer();

            SocketError Connection = await TrySocksConnection(UriHostname, UriPort, TargetSocket);

            if (Connection != SocketError.Success)
            {
                if (Connection == SocketError.HostUnreachable || Connection == SocketError.ConnectionRefused || Connection == SocketError.ConnectionReset)
                    Send(Socket, $"{HttpVersion} 502 Bad Gateway\r\n\r\n");
                else if (Connection == SocketError.AccessDenied)
                    Send(Socket, $"{HttpVersion} 401 Unauthorized\r\n\r\n");
                else
                    Send(Socket, $"{HttpVersion} 500 Internal Server Error\r\nX-Proxy-Error-Type: {Connection}\r\n\r\n");

                Dispose(Socket);
                Dispose(TargetSocket);
            }
            else
            {
                Send(Socket, $"{HttpVersion} 200 Connection established\r\n\r\n");

                Relay(Socket, TargetSocket, false);
            }
        }

        private async Task<SocketError> TrySocksConnection(string DestinationAddress, int DestinationPort, Socket Socket)
        {
            try
            {
                Socket.Connect(new IPEndPoint(Host, Port));

                if (Type == ProxyType.Socks4)
                    return await SendSocks4(Socket, DestinationAddress, DestinationPort).ConfigureAwait(false);
                else if (Type == ProxyType.Socks5)
                    return await SendSocks5(Socket, DestinationAddress, DestinationPort).ConfigureAwait(false);

                return SocketError.ProtocolNotSupported;
            }
            catch (SocketException ex)
            {
                return ex.SocketErrorCode;
            }
        }

        private void Relay(Socket Source, Socket Target, bool IsTarget)
        {
            try
            {
                if (!IsTarget)
                    Task.Run(() => Relay(Target, Source, true));

                int Read = 0;
                byte[] Buffer = new byte[8192];

                while ((Read = Source.Receive(Buffer, 0, Buffer.Length, SocketFlags.None)) > 0)
                    Target.Send(Buffer, 0, Read, SocketFlags.None);
            }
            catch
            {
                // Ignored
            }
            finally
            {
                if (!IsTarget)
                {
                    Dispose(Source);
                    Dispose(Target);
                }
            }
        }

        private async Task<SocketError> SendSocks4(Socket Socket, string DestinationHost, int DestinationPort)
        {
            byte AddressType = GetAddressType(DestinationHost);

            if (AddressType == AddressTypeDomainName)
                DestinationHost = GetHost(DestinationHost).ToString();

            byte[] Address = GetIPAddressBytes(DestinationHost);
            byte[] Port = GetPortBytes(DestinationPort);
            byte[] UserId = new byte[0];

            byte[] Request = new byte[9];

            Request[0] = (byte)SocksVersion;
            Request[1] = 0x01;
            Address.CopyTo(Request, 4);
            Port.CopyTo(Request, 2);
            UserId.CopyTo(Request, 8);
            Request[8] = 0x00;

            byte[] Response = new byte[8];

            Socket.Send(Request);

            await WaitStream(Socket).ConfigureAwait(false);

            Socket.Receive(Response);

            if (Response[1] != 0x5a)
                return SocketError.ConnectionRefused;

            return SocketError.Success;
        }

        private async Task<SocketError> SendSocks5(Socket Socket, string DestinationHost, int DestinationPort)
        {
            byte[] Response = new byte[255];

            byte[] Auth = new byte[3];
            Auth[0] = (byte)SocksVersion;
            Auth[1] = (byte)1;
            Auth[2] = (byte)0;

            Socket.Send(Auth);

            await WaitStream(Socket).ConfigureAwait(false);

            Socket.Receive(Response);

            if (Response[1] != 0x00)
                return SocketError.ConnectionRefused;

            byte AddressType = GetAddressType(DestinationHost);

            if (AddressType == AddressTypeDomainName)
                DestinationHost = GetHost(DestinationHost).ToString();

            byte[] Address = GetAddressBytes(AddressType, DestinationHost);
            byte[] Port = GetPortBytes(DestinationPort);

            byte[] Request = new byte[4 + Address.Length + 2];

            Request[0] = (byte)SocksVersion;
            Request[1] = 0x01;
            Request[2] = 0x00;
            Request[3] = AddressType;
            Address.CopyTo(Request, 4);
            Port.CopyTo(Request, 4 + Address.Length);

            Socket.Send(Request);

            await WaitStream(Socket).ConfigureAwait(false);

            Socket.Receive(Response);

            if (Response[1] != 0x00)
                return SocketError.ConnectionRefused;

            return SocketError.Success;
        }

        private async Task WaitStream(Socket Socket)
        {
            int Sleep = 0;
            int Delay = (Socket.ReceiveTimeout < 10) ? 10 : Socket.ReceiveTimeout;

            while (Socket.Available == 0)
            {
                if (Sleep < Delay)
                {
                    Sleep += 10;
                    await Task.Delay(10).ConfigureAwait(false);

                    continue;
                }

                throw new Exception($"Timeout waiting for data - {Host}:{Port}");
            }
        }

        private Socket CreateSocketServer()
        {
            Socket Socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);

            Socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, 0);
            Socket.ExclusiveAddressUse = true;

            Socket.ReceiveTimeout = Socket.SendTimeout = ReadWriteTimeOut;

            return Socket;
        }

        private void Send(Socket Socket, string Message)
        {
            Socket.Send(Encoding.UTF8.GetBytes(Message));
        }

        private IPAddress GetHost(string Host)
        {
            if (IPAddress.TryParse(Host, out IPAddress Ip))
                return Ip;

            return Dns.GetHostAddresses(Host)[0];
        }

        private byte[] GetAddressBytes(byte AddressType, string Host)
        {
            switch (AddressType)
            {
                case AddressTypeIPV4:
                case AddressTypeIPV6:
                    return IPAddress.Parse(Host).GetAddressBytes();
                case AddressTypeDomainName:
                    byte[] Bytes = new byte[Host.Length + 1];

                    Bytes[0] = (byte)Host.Length;
                    Encoding.ASCII.GetBytes(Host).CopyTo(Bytes, 1);

                    return Bytes;
                default:
                    return null;
            }
        }

        private byte GetAddressType(string Host)
        {
            if (IPAddress.TryParse(Host, out IPAddress Ip))
            {
                if (Ip.AddressFamily == AddressFamily.InterNetwork)
                    return AddressTypeIPV4;

                return AddressTypeIPV6;
            }

            return AddressTypeDomainName;
        }

        private byte[] GetIPAddressBytes(string DestinationHost)
        {
            IPAddress Address = null;

            if (!IPAddress.TryParse(DestinationHost, out Address))
            {
                IPAddress[] IPs = Dns.GetHostAddresses(DestinationHost);

                if (IPs.Length > 0)
                    Address = IPs[0];
            }

            return Address.GetAddressBytes();
        }

        private byte[] GetPortBytes(int Port)
        {
            byte[] ArrayBytes = new byte[2];

            ArrayBytes[0] = (byte)(Port / 256);
            ArrayBytes[1] = (byte)(Port % 256);

            return ArrayBytes;
        }

        private void Dispose(Socket Socket)
        {
            try
            {
                Socket.Close();
                Socket.Dispose();
            }
            catch
            {
                // Ignore
            }
        }

        public void Dispose()
        {
            if (InternalSocketServer != null && !IsDisposed)
            {
                IsDisposed = true;

                InternalSocketServer.Disconnect(false);
                InternalSocketServer.Dispose();
            }
        }
    }
}
