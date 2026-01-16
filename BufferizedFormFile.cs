using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer
{
    public class BufferizedFormFile : IFormFile
    {
        private IFormFile _formFile;
        private MemoryStream _content;
        public byte[] Buffer { get; }
        BufferizedFormFile(IFormFile formFile, byte[] content)
        {
            _formFile = formFile;
            Buffer = content;
            _content = new MemoryStream(content);
        }

        public string ContentType => _formFile.ContentType;

        public string ContentDisposition => _formFile.ContentDisposition;

        public IHeaderDictionary Headers => _formFile.Headers;

        public long Length => _formFile.Length;

        public string Name => _formFile.Name;

        public string FileName => _formFile.FileName;

        public static async Task<BufferizedFormFile> Bufferize(IFormFile formFile)
        {
            if (formFile is BufferizedFormFile b)
                return b;
            var content = new byte[formFile.Length];
            using var fs = formFile.OpenReadStream();
            await fs.ReadAsync(content, 0, content.Length);
            return new BufferizedFormFile(formFile, content);
        }

        public void CopyTo(Stream target)
        {
            _content.CopyTo(target);
        }

        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        {
            return _content.CopyToAsync(target, cancellationToken);
        }

        public Stream OpenReadStream()
        {
            return _content;
        }

        public void Rewind()
        {
            _content.Seek(0, SeekOrigin.Begin);
        }
    }
}
