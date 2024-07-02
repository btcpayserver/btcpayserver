#nullable enable
using System;

namespace BTCPayServer
{
    public record UnresolvedUri(string? FileId)
    {
        public string? GetFileId() => string.IsNullOrEmpty(FileId) ? null : FileId;

        public static UnresolvedUri Create(string str)
        {
            ArgumentNullException.ThrowIfNull(str);
            if (str.StartsWith("fileid:", StringComparison.OrdinalIgnoreCase))
            {
                return new FileIdUri(str.Substring("fileid:".Length));
            }
            return new Raw(str);
        }
        public record FileIdUri(string FileId) : UnresolvedUri(FileId)
        {
            public override string ToString() => $"fileid:{FileId}";
        }
        public record Raw(string Uri) : UnresolvedUri(string.Empty)
        {
            public override string ToString() => Uri;
        }
    }
}
