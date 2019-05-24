using System;
using System.IO;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;

namespace BTCPayServer.Storage.Services.Providers.FileSystemStorage
{
    public class TemporaryLocalFileProvider : IFileProvider
    {
        private readonly DirectoryInfo _fileRoot;
        private readonly StoredFileRepository _storedFileRepository;
        private readonly DirectoryInfo _root;

        public TemporaryLocalFileProvider(DirectoryInfo tmpRoot, DirectoryInfo fileRoot, StoredFileRepository storedFileRepository)
        {
            _fileRoot = fileRoot;
            _storedFileRepository = storedFileRepository;
            _root = tmpRoot;
        }
        public IFileInfo GetFileInfo(string tmpFileId)
        {
            tmpFileId =tmpFileId.TrimStart('/', '\\');
            var path = Path.Combine(_root.FullName,tmpFileId) ;
            if (!File.Exists(path))
            {
                return new NotFoundFileInfo(tmpFileId);
            }

            var text = File.ReadAllText(path);
            var descriptor = JsonConvert.DeserializeObject<TemporaryLocalFileDescriptor>(text);
            if (descriptor.Expiry < DateTime.Now)
            {
                File.Delete(path);
                return new NotFoundFileInfo(tmpFileId);
            }

            var storedFile = _storedFileRepository.GetFile(descriptor.FileId).GetAwaiter().GetResult();
            return new PhysicalFileInfo(new FileInfo(Path.Combine(_fileRoot.FullName, storedFile.StorageFileName)));
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            throw new System.NotImplementedException();
        }

        public IChangeToken Watch(string filter)
        {
            throw new System.NotImplementedException();
        }
    }
}
