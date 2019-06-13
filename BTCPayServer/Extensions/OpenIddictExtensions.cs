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
        public static OpenIddictServerBuilder ConfigureSigningKey(this OpenIddictServerBuilder builder,
            IConfiguration configuration)
        {
            
            var file = Path.Combine(configuration.GetDataDir(), "rsaparams");
                    
            RSACryptoServiceProvider RSA = new RSACryptoServiceProvider(2048);
            RsaSecurityKey key = null;
                    
            if (File.Exists(file))
            {
                RSA.FromXmlString2( File.ReadAllText(file)); 
            }
            else
            {
                var contents = RSA.ToXmlString2(true);
                File.WriteAllText(file,contents );
            }

            RSAParameters KeyParam = RSA.ExportParameters(true);
            key = new RsaSecurityKey(KeyParam);
            return builder.AddSigningKey(key);
        }
    }
}
