using System.IO;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer
{
    public static class IFormFileExtensions
    {
        public static bool IsValid(this IFormFile file)
        {
            return file.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) == -1;
        }
    }
}
