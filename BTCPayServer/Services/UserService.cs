#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using BTCPayServer.Data;
using BTCPayServer.Storage.Services;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Services
{
    public class UserService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuthorizationService _authorizationService;
        private readonly StoredFileRepository _storedFileRepository;
        private readonly FileService _fileService;
        private readonly StoreRepository _storeRepository;

        public UserService(
            UserManager<ApplicationUser> userManager,
            IAuthorizationService authorizationService,
            StoredFileRepository storedFileRepository,
            FileService fileService,
            StoreRepository storeRepository
        )
        {
            _userManager = userManager;
            _authorizationService = authorizationService;
            _storedFileRepository = storedFileRepository;
            _fileService = fileService;
            _storeRepository = storeRepository;
        }

        public async Task<bool> IsAdminUser(ApplicationUser user) 
        {
            return IsRoleAdmin(await _userManager.GetRolesAsync(user));
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

        public bool IsRoleAdmin(IList<string> roles)
        {
            return roles.Contains(Roles.ServerAdmin, StringComparer.Ordinal);
        }
    }
}
