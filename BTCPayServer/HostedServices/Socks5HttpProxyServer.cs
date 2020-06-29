using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Socks;

namespace BTCPayServer.HostedServices
{

    /// <summary>
    /// This is a very simple Socks HTTP proxy, that can be used through HttpClient.WebProxy
    /// However, it only supports a single request/response, so the client must specify Connection: close to not
    /// reuse the TCP connection to the proxy for another requests. 
    /// Inspired from https://devblogs.microsoft.com/dotnet/system-io-pipelines-high-performance-io-in-net/
    /// </summary>
    public class Socks5HttpProxyServer : IHostedService
    {
        class ProxyConnection
        {
            public ServerContext ServerContext;
            public Socket ClientSocket;
            public Socket SocksSocket;
            public CancellationToken CancellationToken;
            public CancellationTokenSource CancellationTokenSource;

            public void Dispose()
            {
                Socks5HttpProxyServer.Dispose(ClientSocket);
                Socks5HttpProxyServer.Dispose(SocksSocket);
                CancellationTokenSource.Dispose();
            }
        }

        class ServerContext
        {
            public EndPoint SocksEndpoint;
            public Socket ServerSocket;
            public CancellationToken CancellationToken;
            public int ConnectionCount;
        }
        private readonly BTCPayServerOptions _opts;

        public Socks5HttpProxyServer(Configuration.BTCPayServerOptions opts)
        {
            _opts = opts;
        }
        private ServerContext _ServerContext;
        private CancellationTokenSource _Cts;
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_opts.SocksEndpoint is null || _ServerContext != null)
                return Task.CompletedTask;
            _Cts = new CancellationTokenSource();
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            Port = ((IPEndPoint)(socket.LocalEndPoint)).Port;
            Uri = new Uri($"http://127.0.0.1:{Port}");
            socket.Listen(5);
            _ServerContext = new ServerContext()
            {
                SocksEndpoint = _opts.SocksEndpoint,
                ServerSocket = socket,
                CancellationToken = _Cts.Token,
                ConnectionCount = 0
            };
            socket.BeginAccept(Accept, _ServerContext);
            Logs.PayServer.LogInformation($"Internal Socks HTTP Proxy listening at {Uri}");
            return Task.CompletedTask;
        }

        public int Port { get; private set; }
        public Uri Uri { get; private set; }

        static void Accept(IAsyncResult ar)
        {
            var ctx = (ServerContext)ar.AsyncState;
            Socket clientSocket = null;
            try
            {
                clientSocket = ctx.ServerSocket.EndAccept(ar);
            }
            catch (Exception)
            {
                return;
            }
            if (ctx.CancellationToken.IsCancellationRequested)
            {
                Dispose(clientSocket);
                return;
            }
            var toSocksProxy = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
            toSocksProxy.BeginConnect(ctx.SocksEndpoint, ConnectToSocks, new ProxyConnection()
            {
                ServerContext = ctx,
                ClientSocket = clientSocket,
                SocksSocket = toSocksProxy,
                CancellationToken = connectionCts.Token,
                CancellationTokenSource = connectionCts
            });
            try
            {
                ctx.ServerSocket.BeginAccept(Accept, ctx);
            }
            catch (Exception)
            {
                return;
            }
        }

        static void ConnectToSocks(IAsyncResult ar)
        {
            var connection = (ProxyConnection)ar.AsyncState;
            try
            {
                connection.SocksSocket.EndConnect(ar);
            }
            catch (Exception)
            {
                connection.Dispose();
                return;
            }
            Interlocked.Increment(ref connection.ServerContext.ConnectionCount);
            var pipe = new Pipe(PipeOptions.Default);
            var reading = FillPipeAsync(connection.ClientSocket, pipe.Writer, connection.CancellationToken)
                .ContinueWith(_ => connection.CancellationTokenSource.Cancel(), TaskScheduler.Default);
            var writing = ReadPipeAsync(connection.SocksSocket, connection.ClientSocket, pipe.Reader, connection.CancellationToken)
                .ContinueWith(_ => connection.CancellationTokenSource.Cancel(), TaskScheduler.Default);
            _ = Task.WhenAll(reading, writing)
                .ContinueWith(_ =>
                {
                    connection.Dispose();
                    Interlocked.Decrement(ref connection.ServerContext.ConnectionCount);
                }, TaskScheduler.Default);
        }

        public int ConnectionCount => _ServerContext is ServerContext s ? s.ConnectionCount : 0;
        private static async Task ReadPipeAsync(Socket socksSocket, Socket clientSocket, PipeReader reader, CancellationToken cancellationToken)
        {
            bool handshaked = false;
            bool isConnect = false;
            string firstHeader = null;
            string httpVersion = null;
            while (true)
            {
                ReadResult result = await reader.ReadAsync(cancellationToken);
                ReadOnlySequence<byte> buffer = result.Buffer;
                SequencePosition? position = null;

                if (!handshaked)
                {
nextchunk:
// Look for a EOL in the buffer
                    position = buffer.PositionOf((byte)'\n');
                    if (position == null)
                        goto readnext;
                    // Process the line
                    var line = GetHeaderLine(buffer.Slice(0, position.Value));
                    // Skip the line + the \n character (basically position)
                    buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
                    if (firstHeader is null)
                    {
                        firstHeader = line;
                        isConnect = line.StartsWith("CONNECT ", StringComparison.OrdinalIgnoreCase);
                        if (isConnect)
                            goto nextchunk;
                        else
                            goto handshake;
                    }
                    else if (line.Length == 1 && line[0] == '\r')
                        goto handshake;
                    else
                        goto nextchunk;

handshake:
                    var split = firstHeader.Split(' ');
                    if (split.Length != 3)
                        break;
                    var targetConnection = split[1].Trim();
                    EndPoint destinationEnpoint = null;
                    if (isConnect)
                    {
                        if (!Utils.TryParseEndpoint(targetConnection,
                            80,
                            out destinationEnpoint))
                            break;
                    }
                    else
                    {
                        if (!System.Uri.TryCreate(targetConnection, UriKind.Absolute, out var uri) ||
                            (uri.Scheme != "http" && uri.Scheme != "https"))
                            break;
                        if (!Utils.TryParseEndpoint($"{uri.DnsSafeHost}:{uri.Port}",
                            uri.Scheme == "http" ? 80 : 443,
                            out destinationEnpoint))
                            break;
                        firstHeader = $"{split[0]} {uri.PathAndQuery} {split[2].TrimEnd()}";
                    }

                    httpVersion = split[2].Trim();
                    try
                    {
                        await NBitcoin.Socks.SocksHelper.Handshake(socksSocket, destinationEnpoint, cancellationToken);
                        handshaked = true;
                        if (isConnect)
                        {
                            await SendAsync(clientSocket,
                                $"{httpVersion} 200 Connection established\r\nConnection: close\r\n\r\n",
                                cancellationToken);
                        }
                        else
                        {
                            await SendAsync(socksSocket, $"{firstHeader}\r\n", cancellationToken);
                            foreach (ReadOnlyMemory<byte> segment in buffer)
                            {
                                await socksSocket.SendAsync(segment, SocketFlags.None, cancellationToken);
                            }
                            buffer = buffer.Slice(buffer.End);
                        }
                        _ = Relay(socksSocket, clientSocket, cancellationToken);
                    }
                    catch (SocksException e) when (e.SocksErrorCode == SocksErrorCode.HostUnreachable || e.SocksErrorCode == SocksErrorCode.HostUnreachable)
                    {
                        await SendAsync(clientSocket, $"{httpVersion} 502 Bad Gateway\r\nContent-Length: 0\r\n\r\n", cancellationToken);
                        goto done;
                    }
                    catch (SocksException e)
                    {
                        await SendAsync(clientSocket, $"{httpVersion} 500 Internal Server Error\r\nContent-Length: 0\r\nX-Proxy-Error-Type: Socks {e.SocksErrorCode}\r\n\r\n", cancellationToken);
                        goto done;
                    }
                    catch (SocketException e)
                    {
                        await SendAsync(clientSocket, $"{httpVersion} 500 Internal Server Error\r\nContent-Length: 0\r\nX-Proxy-Error-Type: Socket {e.SocketErrorCode}\r\n\r\n", cancellationToken);
                        goto done;
                    }
                    catch
                    {
                        await SendAsync(clientSocket, $"{httpVersion} 500 Internal Server Error\r\n\r\n", cancellationToken);
                        goto done;
                    }
                }
                else
                {
                    foreach (ReadOnlyMemory<byte> segment in buffer)
                    {
                        await socksSocket.SendAsync(segment, SocketFlags.None, cancellationToken);
                    }
                    buffer = buffer.Slice(buffer.End);
                }

readnext:
// Tell the PipeReader how much of the buffer we have consumed
                reader.AdvanceTo(buffer.Start, buffer.End);
                // Stop reading if there's no more data coming
                if (result.IsCompleted)
                {
                    break;
                }
            }

done:
// Mark the PipeReader as complete
            reader.Complete();
        }

        private const int BufferSize = 1024 * 5;
        private static async Task Relay(Socket from, Socket to, CancellationToken cancellationToken)
        {
            using var buffer = MemoryPool<byte>.Shared.Rent(BufferSize);
            while (true)
            {
                int bytesRead = await from.ReceiveAsync(buffer.Memory, SocketFlags.None, cancellationToken);
                if (bytesRead == 0)
                    break;
                await to.SendAsync(buffer.Memory.Slice(0, bytesRead), SocketFlags.None, cancellationToken);
            }
        }

        private static async Task SendAsync(Socket clientSocket, string data, CancellationToken cancellationToken)
        {
            var bytes = new byte[Encoding.ASCII.GetByteCount(data)];
            Encoding.ASCII.GetBytes(data, bytes);
            await clientSocket.SendAsync(bytes, SocketFlags.None, cancellationToken);
        }

        private static string GetHeaderLine(ReadOnlySequence<byte> buffer)
        {
            if (buffer.IsSingleSegment)
            {
                return Encoding.ASCII.GetString(buffer.First.Span);
            }

            return string.Create((int)buffer.Length, buffer, (span, sequence) =>
            {
                foreach (var segment in sequence)
                {
                    Encoding.ASCII.GetChars(segment.Span, span);

                    span = span.Slice(segment.Length);
                }
            });
        }

        private static async Task FillPipeAsync(Socket socket, PipeWriter writer, CancellationToken cancellationToken)
        {
            while (true)
            {
                Memory<byte> memory = writer.GetMemory(BufferSize);
                int bytesRead = await socket.ReceiveAsync(memory, SocketFlags.None, cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }
                writer.Advance(bytesRead);
                FlushResult result = await writer.FlushAsync(cancellationToken);
                if (result.IsCompleted)
                {
                    break;
                }
            }
            writer.Complete();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (_ServerContext is ServerContext ctx)
            {
                _Cts.Cancel();
                Dispose(ctx.ServerSocket);
                Logs.PayServer.LogInformation($"Internal Socks HTTP Proxy closed");
            }
            return Task.CompletedTask;
        }

        static void Dispose(Socket s)
        {
            try
            {
                s.Shutdown(SocketShutdown.Both);
                s.Close();
            }
            catch (Exception)
            {

            }
            finally
            {
                s.Dispose();
            }
        }
    }
}
