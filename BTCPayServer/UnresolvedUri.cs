#nullable enable
using System;

namespace BTCPayServer
{
    public record UnresolvedUri
    {
        public static UnresolvedUri Create(string str)
        {
            ArgumentNullException.ThrowIfNull(str);
            if (str.StartsWith("fileid:", StringComparison.OrdinalIgnoreCase))
            {
                return new FileIdUri(str.Substring("fileid:".Length));
            }
            return new Raw(str);
        }
        public record FileIdUri(string FileId) : UnresolvedUri
        {
            public override string ToString() => $"fileid:{FileId}";
        }
        public record Raw(string Uri) : UnresolvedUri
        {
            public override string ToString() => Uri;
        }
    }
}
