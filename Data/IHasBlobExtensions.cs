#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using NBXplorer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public static class IHasBlobExtensions
    {
        static readonly JsonSerializerSettings DefaultSerializerSettings;
        static readonly JsonSerializer DefaultSerializer;
        static IHasBlobExtensions()
        {
            DefaultSerializerSettings = new JsonSerializerSettings()
            {
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
                Formatting = Formatting.None
            };
            NBitcoin.JsonConverters.Serializer.RegisterFrontConverters(DefaultSerializerSettings);
            DefaultSerializer = JsonSerializer.CreateDefault(DefaultSerializerSettings);
        }
        class HasBlobWrapper<B> : IHasBlob<B>
        {
            IHasBlobUntyped data;
            public HasBlobWrapper(IHasBlobUntyped data)
            {
                this.data = data;
            }
            [Obsolete("Use Blob2 instead")]
            public byte[] Blob { get { return data.Blob; } set { data.Blob = value; } }
            public string Blob2 { get { return data.Blob2; } set { data.Blob2 = value; } }
        }
        class HasBlobWrapper : IHasBlob
        {
            IHasBlobUntyped data;
            private Type type;

            public HasBlobWrapper(IHasBlobUntyped data, Type type)
            {
                this.data = data;
                this.type = type;
            }
            [Obsolete("Use Blob2 instead")]
            public byte[] Blob { get => data.Blob; set => data.Blob = value; }
            public string Blob2 { get => data.Blob2; set => data.Blob2 = value; }
            public Type Type { get => type; set => type = value; }
        }

        public static IHasBlob<B> HasTypedBlob<B>(this IHasBlobUntyped data)
        {
            return new HasBlobWrapper<B>(data);
        }
        public static IHasBlob HasTypedBlob(this IHasBlobUntyped data, Type blobType)
        {
            return new HasBlobWrapper(data, blobType);
        }
        public static B? GetBlob<B>(this IHasBlob<B> data, JsonSerializerSettings? settings = null)
        {
            if (data.Blob2 is not null)
                return JObject.Parse(data.Blob2).ToObject<B>(JsonSerializer.CreateDefault(settings ?? DefaultSerializerSettings));
#pragma warning disable CS0618 // Type or member is obsolete
            if (data.Blob is not null && data.Blob.Length != 0)
            {
                string str;
                if (data.Blob[0] == 0x7b)
                    str = Encoding.UTF8.GetString(data.Blob);
                else
                    str = ZipUtils.Unzip(data.Blob);
                return JObject.Parse(str).ToObject<B>(JsonSerializer.CreateDefault(settings ?? DefaultSerializerSettings));
            }
#pragma warning restore CS0618 // Type or member is obsolete
            return default;
        }

        public static object? GetBlob(this IHasBlob data, JsonSerializerSettings? settings = null)
        {
            if (data.Blob2 is not null)
                return JObject.Parse(data.Blob2).ToObject(data.Type, JsonSerializer.CreateDefault(settings ?? DefaultSerializerSettings));
#pragma warning disable CS0618 // Type or member is obsolete
            if (data.Blob is not null && data.Blob.Length != 0)
                return JObject.Parse(ZipUtils.Unzip(data.Blob)).ToObject(data.Type, JsonSerializer.CreateDefault(settings ?? DefaultSerializerSettings));
#pragma warning restore CS0618 // Type or member is obsolete
            return default;
        }

        public static T SetBlob<T, B>(this T data, B blob) where T : IHasBlob<B>
        {
            return SetBlob(data, blob, (JsonSerializer?)null);
        }
        public static T SetBlob<T, B>(this T data, B blob, JsonSerializerSettings? settings) where T : IHasBlob<B>
        {
            return SetBlob(data, blob, settings is null ? null : JsonSerializer.CreateDefault(settings));
        }
        public static T SetBlob<T, B>(this T data, B blob, JsonSerializer? settings) where T : IHasBlob<B>
        {
            if (blob is null)
                data.Blob2 = null;
            else
                data.Blob2 = JObject.FromObject(blob, settings ?? DefaultSerializer).ToString(Formatting.None);
#pragma warning disable CS0618 // Type or member is obsolete
            data.Blob = new byte[0];
#pragma warning restore CS0618 // Type or member is obsolete
            return data;
        }
    }
}
