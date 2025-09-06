#nullable enable

using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using BTCPayServer.Abstractions;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data;

public class BaseEntityData
{
    static BaseEntityData()
    {
        Settings = new JsonSerializerSettings()
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
            DefaultValueHandling = DefaultValueHandling.Ignore
        };
        Serializer = JsonSerializer.Create(Settings);
    }
    public static readonly JsonSerializerSettings Settings;
    public static readonly JsonSerializer Serializer;

    /// <summary>
    /// User-defined custom data
    /// </summary>
    [Column("metadata", TypeName = "jsonb")]
    public string Metadata { get; set; } = "{}";

    /// <summary>
    /// Data that is not user-defined, but can be used and extended internally by BTCPay Server or plugins.
    /// </summary>
    [Column("additional_data", TypeName = "jsonb")]
    public string AdditionalData { get; set; } = "{}";

    [Column("created_at", TypeName = "timestamptz")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public T GetAdditionalData<T>() => Serializer.Deserialize<T>(new JsonTextReader(new StringReader(AdditionalData)))
                                       ?? throw new InvalidOperationException("AddtionalData is not an object");

    public void SetAdditionalData<T>(T obj)
    {
        var w = new StringWriter();
        Serializer.Serialize(w, obj);
        w.Flush();
        AdditionalData = w.ToString();
    }
    public static string GenerateId() => Encoders.Base58.EncodeData(RandomUtils.GetBytes(13));
}
