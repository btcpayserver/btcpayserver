using System;
using System.Net.Http;
using System.Threading.Tasks;
using IdentityModel.Client;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.ClientCredentials
{
    /// <summary>
    /// The client credentials flow is not fit to be used on any client side application.
    /// Exposing the client secret in any unprotected storage( such as a js file in the browser)
    /// IS A CRITICAL SECURITY RISK
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            var baseUrl = "http://127.0.0.1:14142";
            var clientId = "x";
            var clientSecret = "y";
            var scope = ""; //  add offline_access to the scope so that you also receive a refresh token
            //  in the response which allows you receive to extend your session without
            //  sending credentials again
            
            var client = new HttpClient();
            var disco = await client.GetDiscoveryDocumentAsync(baseUrl);
            if (disco.IsError)
            {
                Console.WriteLine(disco.Error);
                return;
            }
            
            // request token
            var response = await client.RequestClientCredentialsTokenAsync(new ClientCredentialsTokenRequest
            {
                Address = disco.TokenEndpoint,

                ClientId = clientId,
                ClientSecret = clientSecret,
                Scope = scope
            });

            if (response.IsError)
            {
                Console.WriteLine(response.Error);
                return;
            }

            Console.WriteLine(response.Json);
            
            
            // call api
            client = new HttpClient();
            client.SetBearerToken(response.AccessToken);

            var apiResponse = await client.GetAsync(new Uri(new Uri(baseUrl), "api/v1.0/test").AbsoluteUri);
            if (!apiResponse.IsSuccessStatusCode)
            {
                Console.WriteLine(apiResponse.StatusCode);
            }
            else
            {
                var content = await apiResponse.Content.ReadAsStringAsync();
                Console.WriteLine(content);
                
            }

            
            //renew session by using refresh token
            if (!string.IsNullOrEmpty(scope) && scope.Contains("offline_access"))
            {
                response = await client.RequestRefreshTokenAsync(new RefreshTokenRequest()
                {
                    Address = disco.TokenEndpoint,
                    RefreshToken = response.RefreshToken,
                    ClientId = clientId,
                    ClientSecret = clientSecret
                });
                
                if (response.IsError)
                {
                    Console.WriteLine(response.Error);
                    return;
                }

                Console.WriteLine(response.Json);
            }
            
        }
    }
}
