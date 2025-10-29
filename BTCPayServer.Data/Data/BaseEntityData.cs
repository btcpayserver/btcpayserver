#nullable enable

using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Threading.Tasks;
using BTCPayServer.Abstractions;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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

    public T? GetAdditionalData<T>(string key) where T: class
        => JObject.Parse(AdditionalData)[key]?.ToObject<T>(Serializer);

    [Obsolete("Avoid using this method, if another plugin is modifying additional data at the same time, it would overwrite the changes. Only use if you can ensure that no other plugin is modifying the data at the same time.")]
    public void SetAdditionalData<T>(string key, T? obj)
    {
        if (obj is null)
        {
            RemoveAdditionalData(key);
        }
        else
        {
            var w = new StringWriter();
            Serializer.Serialize(w, obj);
            w.Flush();
            var jobj = JObject.Parse(AdditionalData);
            jobj[key] = JToken.Parse(w.ToString());
            AdditionalData = jobj.ToString();
        }
    }
    public void RemoveAdditionalData(string key)
    {
        var jobj = JObject.Parse(AdditionalData);
        jobj.Remove(key);
        AdditionalData = jobj.ToString();
    }

    public static string GenerateId() => Encoders.Base58.EncodeData(RandomUtils.GetBytes(13));

    protected static void OnModelCreateBase<TEntity>(EntityTypeBuilder<TEntity> b, ModelBuilder builder, DatabaseFacade databaseFacade) where TEntity : BaseEntityData
    {
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz")
            .HasDefaultValueSql("now()");
        b.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");
        b.Property(x => x.AdditionalData).HasColumnName("additional_data").HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");
    }

    public static string ToUpdateAdditionalDataJson<T>(string key, T obj) where T : class
    => new JObject()
    {
        [key] = JObject.FromObject(obj, Serializer)
    }.ToString();
}
