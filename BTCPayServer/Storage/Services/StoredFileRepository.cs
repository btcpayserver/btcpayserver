using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Storage.Models;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Storage.Services
{
    public class StoredFileRepository
    {
        private readonly ApplicationDbContextFactory _ApplicationDbContextFactory;

        public StoredFileRepository(ApplicationDbContextFactory applicationDbContextFactory)
        {
            _ApplicationDbContextFactory = applicationDbContextFactory;
        }

        public async Task<StoredFile> GetFile(string fileId)
        {
            using (var context = _ApplicationDbContextFactory.CreateContext())
            {
                return await context.Files.FindAsync(fileId);
            }
        }
        
        public async Task<List<StoredFile>> GetFiles()
        {
            using (var context = _ApplicationDbContextFactory.CreateContext())
            {
                return await context.Files.ToListAsync();
            }
        }

        public async Task RemoveFile(string fileId)
        {
            var file = await GetFile(fileId);
            if (file == null)
            {
                return;
            }

            using (var context = _ApplicationDbContextFactory.CreateContext())
            {
                context.Attach(file);
                context.Files.Remove(file);
                await context.SaveChangesAsync();
            }
        }

        public async Task AddFile(StoredFile storedFile)
        {
            using (var context = _ApplicationDbContextFactory.CreateContext())
            {
                await context.AddAsync(storedFile);
                await context.SaveChangesAsync();
            }
        }
    }
}