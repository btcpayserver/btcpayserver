using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using NBitcoin.Protocol;
using NBitcoin.Protocol.Connectors;

namespace BTCPayServer.Services
{
    public class SocketFactory
    {
        private readonly BTCPayServerOptions _options;

        public SocketFactory(BTCPayServerOptions options)
        {
            _options = options;
        }

        public async Task<Socket> ConnectAsync(EndPoint endPoint, CancellationToken cancellationToken)
        {
            DefaultEndpointConnector connector = new DefaultEndpointConnector();
            NodeConnectionParameters connectionParameters = new NodeConnectionParameters();
            if (_options.SocksEndpoint != null)
            {
                connectionParameters.TemplateBehaviors.Add(new NBitcoin.Protocol.Behaviors.SocksSettingsBehavior()
                {
                    SocksEndpoint = _options.SocksEndpoint
                });
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await connector.ConnectSocket(socket, endPoint, connectionParameters, cancellationToken);
            }
            catch
            {
                SafeCloseSocket(socket);
            }

            return socket;
        }

        internal static void SafeCloseSocket(Socket socket)
        {
            try
            {
                socket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
            }

            try
            {
                socket.Dispose();
            }
            catch
            {
            }
        }
    }
}
