using System;
using System.Globalization;
using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.SSH
{
    public class SSHFingerprint
    {
        public static bool TryParse(string str, out SSHFingerprint fingerPrint)
        {
            ArgumentNullException.ThrowIfNull(str);
            fingerPrint = null;
            str = str.Trim();
            try
            {
                var shortFingerprint = str.Replace(":", "", StringComparison.OrdinalIgnoreCase);
                if (HexEncoder.IsWellFormed(shortFingerprint))
                {
                    var hash = Encoders.Hex.DecodeData(shortFingerprint);
                    if (hash.Length == 16)
                    {
                        fingerPrint = new SSHFingerprint(hash);
                        return true;
                    }
                    return false;
                }
            }
            catch
            {
            }

            if (str.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
                str = str.Substring("SHA256:".Length).Trim();
            if (str.Contains(':', StringComparison.OrdinalIgnoreCase))
                return false;
            if (!str.EndsWith('='))
                str = str + "=";
            try
            {
                var hash = Encoders.Base64.DecodeData(str);
                if (hash.Length == 32)
                {
                    fingerPrint = new SSHFingerprint(hash);
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        public SSHFingerprint(byte[] hash)
        {
            if (hash.Length == 16)
            {
                _ShortFingerprint = hash;
                _Original = string.Join(':', hash.Select(b => b.ToString("x2", CultureInfo.InvariantCulture))
                    .ToArray());
            }
            else if (hash.Length == 32)
            {
                _FullHash = hash;
                _Original = "SHA256:" + Encoders.Base64.EncodeData(hash);
                if (_Original.EndsWith("=", StringComparison.OrdinalIgnoreCase))
                    _Original = _Original.Substring(0, _Original.Length - 1);
            }
            else
                throw new ArgumentException(paramName: nameof(hash), message: "Invalid length, expected 16 or 32");
        }

        readonly byte[] _ShortFingerprint;
        readonly byte[] _FullHash;

        public bool Match(byte[] shortFingerprint, byte[] hostKey)
        {
            ArgumentNullException.ThrowIfNull(shortFingerprint);
            ArgumentNullException.ThrowIfNull(hostKey);
            if (_ShortFingerprint != null)
                return Utils.ArrayEqual(shortFingerprint, _ShortFingerprint);
            return Utils.ArrayEqual(_FullHash, NBitcoin.Crypto.Hashes.SHA256(hostKey));
        }

        readonly string _Original;
        public override string ToString()
        {
            return _Original;
        }
    }
}
