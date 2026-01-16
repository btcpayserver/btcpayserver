#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.AccessControl;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Services
{
    public class UriResolver
    {
        private readonly IFileService _fileService;

        public UriResolver(IFileService fileService)
        {
            _fileService = fileService;
        }

        /// <summary>
        /// If <paramref name="url"/> is an absolute URL, returns it as is.
        /// If <paramref name="url"/> starts with "fileid:ID", returns the URL of the file with the ID.
        /// </summary>
        /// <param name="baseUri"><see cref="BTCPayServer.Abstractions.Extensions.HttpRequestExtensions.GetAbsoluteRootUri"/></param>
        /// <param name="uri"></param>
        /// <returns></returns>
        public async Task<string?> Resolve(Uri baseUri, UnresolvedUri? uri)
        {
            return uri switch
            {
                null => null,
                UnresolvedUri.FileIdUri fileId => await _fileService.GetFileUrl(baseUri, fileId.FileId),
                UnresolvedUri.Raw raw => raw.Uri,
                _ => throw new NotSupportedException(uri.GetType().Name)
            };
        }
    }
}
