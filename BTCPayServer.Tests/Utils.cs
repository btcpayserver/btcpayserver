using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace BTCPayServer.Tests
{
    public class Utils
    {
        public static int _nextPort = 8001;
        public static object _portLock = new object();

        public static int FreeTcpPort()
        {
            lock (_portLock)
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    while (true)
                    {
                        try
                        {
                            var port = _nextPort++;
                            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
                            return port;
                        }
                        catch (SocketException)
                        {
                            // Retry unless exhausted
                            if (_nextPort == 65536)
                            {
                                throw;
                            }
                        }
                    }
                }
            }
        }

        // http://stackoverflow.com/a/14933880/2061103
        public static void DeleteDirectory(string destinationDir)
        {
            const int magicDust = 10;
            for (var gnomes = 1; gnomes <= magicDust; gnomes++)
            {
                try
                {
                    Directory.Delete(destinationDir, true);
                }
                catch (DirectoryNotFoundException)
                {
                    return;  // good!
                }
                catch (IOException)
                {
                    if (gnomes == magicDust)
                        throw;
                    // System.IO.IOException: The directory is not empty
                    System.Diagnostics.Debug.WriteLine("Gnomes prevent deletion of {0}! Applying magic dust, attempt #{1}.", destinationDir, gnomes);

                    // see http://stackoverflow.com/questions/329355/cannot-delete-directory-with-directory-deletepath-true for more magic
                    Thread.Sleep(100);
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    if (gnomes == magicDust)
                        throw;
                    // Wait, maybe another software make us authorized a little later
                    System.Diagnostics.Debug.WriteLine("Gnomes prevent deletion of {0}! Applying magic dust, attempt #{1}.", destinationDir, gnomes);

                    // see http://stackoverflow.com/questions/329355/cannot-delete-directory-with-directory-deletepath-true for more magic
                    Thread.Sleep(100);
                    continue;
                }
                return;
            }
            // depending on your use case, consider throwing an exception here
        }
    }
}
