#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using BTCPayServer.Storage.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Services
{
    public class UserService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuthorizationService _authorizationService;
        private readonly StoredFileRepository _storedFileRepository;
        private readonly FileService _fileService;
        private readonly StoreRepository _storeRepository;
        private readonly ApplicationDbContextFactory _applicationDbContextFactory;

        public UserService(
            UserManager<ApplicationUser> userManager,
            IAuthorizationService authorizationService,
            StoredFileRepository storedFileRepository,
            FileService fileService,
            StoreRepository storeRepository,
            ApplicationDbContextFactory applicationDbContextFactory
            
        )
        {
            _userManager = userManager;
            _authorizationService = authorizationService;
            _storedFileRepository = storedFileRepository;
            _fileService = fileService;
            _storeRepository = storeRepository;
            _applicationDbContextFactory = applicationDbContextFactory;
        }

        public async Task<List<ApplicationUserData>> GetUsersWithRoles()
        {
            await using var context = _applicationDbContextFactory.CreateContext();
            return await  (context.Users.Select(p => FromModel(p, p.UserRoles.Join(context.Roles, userRole => userRole.RoleId, role => role.Id,
                (userRole, role) => role.Name).ToArray()))).ToListAsync();
        }
        
        
        public static ApplicationUserData FromModel(ApplicationUser data, string[] roles)
        {
            return new ApplicationUserData()
            {
                Id = data.Id,
                Email = data.Email,
                EmailConfirmed = data.EmailConfirmed,
                RequiresEmailConfirmation = data.RequiresEmailConfirmation,
                Created = data.Created,
                Roles = roles
            };
        }

        public async Task<bool> IsAdminUser(string userId)
        {
            return IsRoleAdmin(await _userManager.GetRolesAsync(new ApplicationUser() { Id = userId }));
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
