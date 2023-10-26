using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using Newtonsoft.Json;

namespace BTCPayServer.Common.Altcoins.Chia.RPC
{
    /// <summary>
    /// The messaging data structure for request and response exchange with the RPC endpoint
    /// </summary>
    public record Message
    {
        /// <summary>
        /// The command to be processed by the endpoint service
        /// </summary>
        public string Command { get; init; } = string.Empty;

        /// <summary>
        /// Data to go along with the command
        /// </summary>
        public dynamic? Data { get; init; }

        /// <summary>
        /// The name of the origin service
        /// </summary>
        public string Origin { get; init; } = string.Empty;

        /// <summary>
        /// The name of the destination service
        /// </summary>
        public string Destination { get; init; } = string.Empty;

        /// <summary>
        /// Indication whether message is an acknowledgement (i.e response)
        /// </summary>
        public bool Ack { get; init; }

        /// <summary>
        /// Unique correlation id of the message. This will round trip to the RPC server and back in its response
        /// </summary>
        public string RequestId { get; init; } = string.Empty;

        /// <summary>
        /// Inidcates whether this is a response (<see cref="Ack"/> is true) and the success flag is also true
        /// </summary>
        [JsonIgnore]
        public bool IsSuccessfulResponse => Ack && Data?.success == true;

        /// <summary>
        /// Construct a new instance of a <see cref="Message"/>
        /// </summary>
        /// <param name="command"><see cref="Command"/></param>
        /// <param name="data"><see cref="Data"/></param>
        /// <param name="destination"><see cref="Destination"/></param>
        /// <param name="origin"><see cref="Origin"/></param>
        /// <returns>A populated <see cref="Message"/></returns>
        /// <remarks>Ensure that <see cref="Data"/> and <see cref="RequestId"/> are set appropriately</remarks>
        public static Message Create(string command, object? data, string destination, string origin)
        {
            return string.IsNullOrEmpty(command)
                ? throw new ArgumentNullException(nameof(command))
                : string.IsNullOrEmpty(destination)
                ? throw new ArgumentNullException(nameof(destination))
                : string.IsNullOrEmpty(origin)
                ? throw new ArgumentNullException(nameof(origin))
                : new Message
                {
                    Command = command,
                    Data = FormatDataObject(data),
                    Origin = origin,
                    Destination = destination,
                    RequestId = GetNewReuqestId()
                };
        }

        private static dynamic FormatDataObject(object? data)
        {
            if (data is null)
            {
                return new ExpandoObject();
            }

            // this will prune any expando object fields that are null
            // do this here to avoid null checks as data objects are constructed
            var dict = data as IDictionary<string, object>;
            if (dict is not null)
            {
                var nonNullProperties = dict.Where(kvp => kvp.Value != null)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                var newDict = new ExpandoObject() as IDictionary<string, object>;
                foreach (var property in nonNullProperties)
                {
                    newDict.Add(property);
                }

                return newDict;
            }

            // input is not an expando - just return the static type
            return data;
        }

        private static readonly Random random = new();

        private static string GetNewReuqestId()
        {
            var buffer = new byte[32];
            random.NextBytes(buffer);
            return Convert.ToHexString(buffer);
        }
    }
}
