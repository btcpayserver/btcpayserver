using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Sdk;

namespace BTCPayServer.Tests
{
    public class FactWithSecretAttribute : FactAttribute
    {
        public FactWithSecretAttribute(string secret)
        {
            try
            {
                GetFromSecrets(secret);
            }
            catch (XunitException ex)
            {
                Skip = ex.Message;
            }
        }
        public static string GetFromSecrets(string key)
        {
            var connStr = Environment.GetEnvironmentVariable($"TESTS_{key}");
            if (!string.IsNullOrEmpty(connStr) && connStr != "none")
                return connStr;
            var builder = new ConfigurationBuilder();
            builder.AddUserSecrets("AB0AC1DD-9D26-485B-9416-56A33F268117");
            var config = builder.Build();
            var token = config[key];
            Assert.False(token == null, $"{key} is not set.\n Run \"dotnet user-secrets set {key} <value>\"");
            return token;
        }
    }
}
