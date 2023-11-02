#nullable enable
using System;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3.Model.Internal.MarshallTransformations;
using ExchangeSharp;
using Newtonsoft.Json.Linq;

namespace BTCPayServer
{
    public enum VaultMessageType
    {
        Ok,
        Error,
        Processing
    }
    public enum VaultServices
    {
        HWI,
        NFC
    }

    public class VaultNotConnectedException : VaultException
    {
        public VaultNotConnectedException() : base("BTCPay Vault isn't connected")
        {

        }
    }
    public class VaultException : Exception
    {
        public VaultException(string message) : base(message)
        {

        }
    }
    public class VaultClient
    {
        public VaultClient(WebSocket websocket)
        {
            Websocket = new WebSocketHelper(websocket);
        }

        public WebSocketHelper Websocket { get; }

        public async Task<string> GetNextCommand(CancellationToken cancellationToken)
        {
            return await Websocket.NextMessageAsync(cancellationToken);
        }

        public async Task SendMessage(JObject mess, CancellationToken cancellationToken)
        {
            await Websocket.Send(mess.ToString(), cancellationToken);
        }

        public Task Show(VaultMessageType type, string message, CancellationToken cancellationToken)
        {
            return Show(type, message, null, cancellationToken);
        }
        public async Task Show(VaultMessageType type, string message, string? debug, CancellationToken cancellationToken)
        {

            await SendMessage(new JObject()
            {
                ["command"] = "showMessage",
                ["message"] = message,
                ["type"] = type.ToString(),
                ["debug"] = debug
            }, cancellationToken);
        }

        string? _ServiceUri;
        public async Task<bool?> AskPermission(VaultServices service, CancellationToken cancellationToken)
        {
            var uri = service switch
            {
                VaultServices.HWI => "http://127.0.0.1:65092/hwi-bridge/v1",
                VaultServices.NFC => "http://127.0.0.1:65092/nfc-bridge/v1",
                _ => throw new NotSupportedException()
            };

            await this.SendMessage(new JObject()
            {
                ["command"] = "sendRequest",
                ["uri"] = uri + "/request-permission"
            }, cancellationToken);
            var result = await GetNextMessage(cancellationToken);
            if (result["httpCode"] is { } p)
            {
                var ok = p.Value<int>() == 200;
                if (ok)
                    _ServiceUri = uri;
                return ok;
            }
            return null;
        }

        public async Task<JToken?> SendVaultRequest(string? path, JObject? body, CancellationToken cancellationToken)
        {
            var isAbsolute = path is not null && Uri.IsWellFormedUriString(path, UriKind.Absolute);
            var query = new JObject()
            {
                ["command"] = "sendRequest",
                ["uri"] = isAbsolute ? path : _ServiceUri + path
            };
            if (body is not null)
                query["body"] = body;
            await this.SendMessage(query, cancellationToken);
            var resp = await GetNextMessage(cancellationToken);
            if (resp["httpCode"] is not { } p)
                throw new VaultNotConnectedException();
            if (p.Value<int>() != 200)
                throw new VaultException($"Unexpected response code from vault {p.Value<int>()}");
            return resp["body"] as JToken;
        }

        public async Task<JObject> GetNextMessage(CancellationToken cancellationToken)
        {
            return JObject.Parse(await this.Websocket.NextMessageAsync(cancellationToken));
        }

        public Task SendSimpleMessage(string command, CancellationToken cancellationToken)
        {
            return SendMessage(new JObject() { ["command"] = command }, cancellationToken);
        }
    }
}
