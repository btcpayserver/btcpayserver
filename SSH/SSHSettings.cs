using System.Collections.Generic;
using System.Linq;
using Renci.SshNet;

namespace BTCPayServer.SSH
{
    public class SSHSettings
    {
        public string Server { get; set; }
        public int Port { get; set; } = 22;
        public string KeyFile { get; set; }
        public string KeyFilePassword { get; set; }
        public string AuthorizedKeysFile { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public List<SSHFingerprint> TrustedFingerprints { get; set; } = new List<SSHFingerprint>();
        internal bool IsTrustedFingerprint(byte[] fingerPrint, byte[] hostKey)
        {
            return TrustedFingerprints.Any(f => f.Match(fingerPrint, hostKey));
        }

        public ConnectionInfo CreateConnectionInfo()
        {
            if (!string.IsNullOrEmpty(KeyFile))
            {
                return new ConnectionInfo(Server, Port, Username, new[] { new PrivateKeyAuthenticationMethod(Username, new PrivateKeyFile(KeyFile, KeyFilePassword)) });
            }
            else
            {
                return new ConnectionInfo(Server, Port, Username, new[] { new PasswordAuthenticationMethod(Username, Password) });
            }
        }
    }
}
