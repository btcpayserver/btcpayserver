using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Storage.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.Services
{
    public class UserService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly StoredFileRepository _storedFileRepository;
        private readonly FileService _fileService;
        private readonly StoreRepository _storeRepository;

        public UserService(
            UserManager<ApplicationUser> userManager,
            StoredFileRepository storedFileRepository,
            FileService fileService,
            StoreRepository storeRepository
        )
        {
            _userManager = userManager;
            _storedFileRepository = storedFileRepository;
            _fileService = fileService;
            _storeRepository = storeRepository;
        }

        public async Task DeleteUserAndAssociatedData(ApplicationUser user) 
        {
            var userId = user.Id;
            var files = await _storedFileRepository.GetFiles(new StoredFileRepository.FilesQuery()
            {
                UserIds = new[] { userId },
            });

            await Task.WhenAll(files.Select(file => _fileService.RemoveFile(file.Id, userId)));

            await _userManager.DeleteAsync(user);
            await _storeRepository.CleanUnreachableStores();
        }
    }
}
