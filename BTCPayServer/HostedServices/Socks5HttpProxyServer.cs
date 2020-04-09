using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Lightning.Eclair.Models;
using BTCPayServer.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Socks;

namespace BTCPayServer.HostedServices
{
    // Our implementation follow https://devblogs.microsoft.com/dotnet/system-io-pipelines-high-performance-io-in-net/
    public class Socks5HttpProxyServer : IHostedService
    {
        class ProxyConnection
        {
            public Socket ClientSocket;
            public Socket SocksSocket;
            public CancellationToken CancellationToken;

            public void Dispose()
            {
                Socks5HttpProxyServer.Dispose(ClientSocket);
                Socks5HttpProxyServer.Dispose(SocksSocket);
            }
        }
        private readonly BTCPayServerOptions _opts;

        public Socks5HttpProxyServer(Configuration.BTCPayServerOptions opts)
        {
            _opts = opts;
        }
        private Socket _ServerSocket;
        private CancellationTokenSource _Cts;
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_opts.SocksEndpoint is null)
                return Task.CompletedTask;
            _Cts = new CancellationTokenSource();
            _ServerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _ServerSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            Port = ((IPEndPoint)(_ServerSocket.LocalEndPoint)).Port;
            Uri = new Uri($"http://127.0.0.1:{Port}");
            _ServerSocket.Listen(5);
            _ServerSocket.BeginAccept(Accept, null);
            Logs.PayServer.LogInformation($"Internal Socks HTTP Proxy listening at {Uri}");
            return Task.CompletedTask;
        }

        public int Port { get; private set; }
        public Uri Uri { get; private set; }

        void Accept(IAsyncResult ar)
        {
            Socket clientSocket = null;
            try
            {
                clientSocket = _ServerSocket.EndAccept(ar);
            }
            catch (ObjectDisposedException e)
            {
                return;
            }
            if (_Cts.IsCancellationRequested)
            {
                Dispose(clientSocket);
                return;
            }
            var toSocksProxy  = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            toSocksProxy.BeginConnect(_opts.SocksEndpoint, ConnectToSocks, new ProxyConnection()
            {
                ClientSocket = clientSocket,
                SocksSocket = toSocksProxy,
                CancellationToken = _Cts.Token
            });
            _ServerSocket.BeginAccept(Accept, null);
        }

        void ConnectToSocks(IAsyncResult ar)
        {
            var connection = (ProxyConnection)ar.AsyncState;
            try
            {
                connection.SocksSocket.EndConnect(ar);
            }
            catch (Exception e)
            {
                connection.Dispose();
                return;
            }
            Interlocked.Increment(ref connectionCount);
            var pipe = new Pipe(PipeOptions.Default);
            var reading = FillPipeAsync(connection.ClientSocket, pipe.Writer, connection.CancellationToken);
            var writing = ReadPipeAsync(connection.SocksSocket, connection.ClientSocket, pipe.Reader, connection.CancellationToken);
            _ = Task.WhenAll(reading, writing)
                .ContinueWith(_ =>
                {
                    connection.Dispose();
                    Interlocked.Decrement(ref connectionCount);
                });
        }

        private int connectionCount = 0;
        public int ConnectionCount => connectionCount;
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
                        await SendAsync(clientSocket , $"{httpVersion} 502 Bad Gateway\r\n\r\n", cancellationToken);
                    }
                    catch (SocksException e)
                    {
                        await SendAsync(clientSocket , $"{httpVersion} 500 Internal Server Error\r\nX-Proxy-Error-Type: Socks {e.SocksErrorCode}\r\n\r\n", cancellationToken);
                    }
                    catch (SocketException e)
                    {
                        await SendAsync(clientSocket , $"{httpVersion} 500 Internal Server Error\r\nX-Proxy-Error-Type: Socket {e.SocketErrorCode}\r\n\r\n", cancellationToken);
                    }
                    catch
                    {
                        await SendAsync(clientSocket , $"{httpVersion} 500 Internal Server Error\r\n\r\n", cancellationToken);
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
                // Allocate at least 512 bytes from the PipeWriter
                Memory<byte> memory = writer.GetMemory(BufferSize);
                try 
                {
                    int bytesRead = await socket.ReceiveAsync(memory, SocketFlags.None, cancellationToken);
                    if (bytesRead == 0)
                    {
                        break;
                    }
                    // Tell the PipeWriter how much was read from the Socket
                    writer.Advance(bytesRead);
                }
                catch (Exception ex)
                {
                    //LogError(ex);
                    break;
                }

                // Make the data available to the PipeReader
                FlushResult result = await writer.FlushAsync(cancellationToken);

                if (result.IsCompleted)
                {
                    break;
                }
            }

            // Tell the PipeReader that there's no more data coming
            writer.Complete();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (_ServerSocket is Socket)
            {
                _Cts.Cancel();
                Dispose(_ServerSocket);
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
