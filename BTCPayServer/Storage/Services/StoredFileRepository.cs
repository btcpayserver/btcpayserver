using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
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
            var filesResult = await GetFiles(new FilesQuery() { Id = new string[] { fileId } });
            return filesResult.FirstOrDefault();
        }

        public async Task<List<StoredFile>> GetFiles(FilesQuery filesQuery = null)
        {
            if (filesQuery == null)
            {
                filesQuery = new FilesQuery();
            }

            using (var context = _ApplicationDbContextFactory.CreateContext())
            {
                return await context.Files
                    .Include(file => file.ApplicationUser)
                    .Where(file =>
                        (!filesQuery.Id.Any() || filesQuery.Id.Contains(file.Id)) &&
                        (!filesQuery.UserIds.Any() || filesQuery.UserIds.Contains(file.ApplicationUserId)))
                    .OrderByDescending(file => file.Timestamp)
                    .ToListAsync();
            }
        }

        public async Task RemoveFile(StoredFile file)
        {
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

        public class FilesQuery
        {
            public string[] Id { get; set; } = Array.Empty<string>();
            public string[] UserIds { get; set; } = Array.Empty<string>();
        }
    }
}
