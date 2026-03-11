using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Tests;

public class MailPitClient
{
    private readonly HttpClient _client;

    public MailPitClient(HttpClient client)
    {
        _client = client;
    }

    public async Task<Message> GetMessage(string id)
    {
        var result = await _client.GetStringAsync($"api/v1/message/{id}");
        var settings = new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.None // let the converter handle "Date"
        };
        return JsonConvert.DeserializeObject<Message>(result, settings);
    }

    public sealed class Message
    {
        [JsonProperty("Attachments")]
        public List<Attachment> Attachments { get; set; }

        [JsonProperty("Bcc")]
        public List<MailAddress> Bcc { get; set; }

        [JsonProperty("Cc")]
        public List<MailAddress> Cc { get; set; }

        [JsonProperty("Date")]
        [JsonConverter(typeof(Rfc3339NanoDateTimeOffsetConverter))]
        public DateTimeOffset? Date { get; set; }

        [JsonProperty("From")]
        public MailAddress From { get; set; }

        [JsonProperty("HTML")]
        public string Html { get; set; }

        [JsonProperty("ID")]
        public string Id { get; set; }

        [JsonProperty("Inline")]
        public List<Attachment> Inline { get; set; }

        [JsonProperty("ListUnsubscribe")]
        public ListUnsubscribeInfo ListUnsubscribe { get; set; }

        [JsonProperty("MessageID")]
        public string MessageId { get; set; }

        [JsonProperty("ReplyTo")]
        public List<MailAddress> ReplyTo { get; set; }

        [JsonProperty("ReturnPath")]
        public string ReturnPath { get; set; }

        [JsonProperty("Size")]
        public int? Size { get; set; }

        [JsonProperty("Subject")]
        public string Subject { get; set; }

        [JsonProperty("Tags")]
        public List<string> Tags { get; set; }

        [JsonProperty("Text")]
        public string Text { get; set; }

        [JsonProperty("To")]
        public List<MailAddress> To { get; set; }

        [JsonProperty("Username")]
        public string Username { get; set; }

        // Capture any unexpected fields without breaking deserialization.
        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; }
    }

    public sealed class Attachment
    {
        [JsonProperty("ContentID")]
        public string ContentId { get; set; }

        [JsonProperty("ContentType")]
        public string ContentType { get; set; }

        [JsonProperty("FileName")]
        public string FileName { get; set; }

        [JsonProperty("PartID")]
        public string PartId { get; set; }

        [JsonProperty("Size")]
        public int? Size { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; }
    }

    public sealed class MailAddress
    {
        [JsonProperty("Address")]
        public string Address { get; set; }

        [JsonProperty("Name")]
        public string Name { get; set; }
    }

    public sealed class ListUnsubscribeInfo
    {
        [JsonProperty("Errors")]
        public string Errors { get; set; }

        [JsonProperty("Header")]
        public string Header { get; set; }

        [JsonProperty("HeaderPost")]
        public string HeaderPost { get; set; }

        [JsonProperty("Links")]
        public List<string> Links { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> Extra { get; set; }
    }

    /// <summary>
    /// Permissive RFC3339/RFC3339Nano converter to DateTimeOffset.
    /// Trims fractional seconds to 7 digits (the .NET limit).
    /// </summary>
    public sealed class Rfc3339NanoDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
    {
        // Matches fractional seconds if present, e.g., .123456789
        private static readonly Regex FractionRegex =
            new Regex(@"\.(\d+)(?=[Zz]|[+\-]\d{2}:\d{2}$)", RegexOptions.Compiled);

        public override void WriteJson(JsonWriter writer, DateTimeOffset? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            // Use ISO 8601 with offset; keep up to 7 fractional digits if needed.
            writer.WriteValue(value.Value.ToString("o", CultureInfo.InvariantCulture));
        }

        public override DateTimeOffset? ReadJson(JsonReader reader, Type objectType, DateTimeOffset? existingValue, bool hasExistingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            if (reader.TokenType != JsonToken.String)
                throw new JsonSerializationException($"Unexpected token {reader.TokenType} when parsing DateTimeOffset.");

            var s = (string)reader.Value;
            if (string.IsNullOrWhiteSpace(s)) return null;

            // Trim fractional seconds to max 7 digits for .NET DateTime parsing.
            s = FractionRegex.Replace(s, m =>
            {
                var frac = m.Groups[1].Value;
                if (frac.Length <= 7) return m.Value; // unchanged
                return "." + frac.Substring(0, 7);
            });

            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
                return dto;

            // Fallback: try without colon in offset (some variants exist)
            s = s.Replace("Z", "+00:00").Replace("z", "+00:00");
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dto))
                return dto;

            throw new JsonSerializationException($"Unable to parse RFC3339 date-time: '{(string)reader.Value}'.");
        }
    }
}
