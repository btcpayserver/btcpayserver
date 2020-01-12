using System.IO;
using System.Security.Cryptography;
using BTCPayServer.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NETCore.Encrypt.Extensions.Internal;

namespace BTCPayServer
{
    public static class OpenIddictExtensions
    {
        public static SecurityKey GetSigningKey(IConfiguration configuration, string fileName)
        {
          
            var file = Path.Combine(configuration.GetDataDir(), fileName);
            using var rsa = new RSACryptoServiceProvider(2048);
            if (File.Exists(file))
            {
                rsa.FromXmlString2(File.ReadAllText(file));
            }
            else
            {
                var contents = rsa.ToXmlString2(true);
                File.WriteAllText(file, contents);
            }
            return new RsaSecurityKey(rsa.ExportParameters(true));;
        }
        public static OpenIddictServerBuilder ConfigureSigningKey(this OpenIddictServerBuilder builder,
            IConfiguration configuration)
        {
            return builder
                    .AddSigningKey(GetSigningKey(configuration, "signing.rsaparams"))
                    .AddEncryptionKey(GetSigningKey(configuration, "encrypting.rsaparams"));
        }
    }
}
