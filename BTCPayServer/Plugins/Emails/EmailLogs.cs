using System;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Plugins.Emails;


public enum EmailLogStatus { Sent, Failed }

public class EmailLogEntry
{
    public static EmailLogEntry Create(string? storeId) => new()
    {
        Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16)),
        StoreId = storeId
    };

    public string Id { get; set; } = null!;
    public string? StoreId { get; set; }
    public DateTime Timestamp { get; set; }
    public EmailLogStatus Status { get; set; }
    public string Trigger { get; set; }
    public string[] To { get; set; } = [];
    public string[] CC { get; set; } = [];
    public string[] BCC { get; set; } = [];
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Error { get; set; }
}

public class EmailLogListItem
{
    public string Id { get; set; } = null!;
    public DateTime Timestamp { get; set; }
    public EmailLogStatus Status { get; set; }
    public string Trigger { get; set; } = "";
    public string[] To { get; set; } = [];
    public string Subject { get; set; } = "";
    public string? Error { get; set; }

    public DateTimeOffset Date => new(DateTime.SpecifyKind(Timestamp, DateTimeKind.Utc));
}
