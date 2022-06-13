#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using BTCPayServer.Storage.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Services
{
    public class UserService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly StoredFileRepository _storedFileRepository;
        private readonly FileService _fileService;
        private readonly StoreRepository _storeRepository;
        private readonly ApplicationDbContextFactory _applicationDbContextFactory;
        private readonly ILogger<UserService> _logger;

        public UserService(
            UserManager<ApplicationUser> userManager,
            StoredFileRepository storedFileRepository,
            FileService fileService,
            StoreRepository storeRepository,
            ApplicationDbContextFactory applicationDbContextFactory,
            ILogger<UserService> logger)
        {
            _userManager = userManager;
            _storedFileRepository = storedFileRepository;
            _fileService = fileService;
            _storeRepository = storeRepository;
            _applicationDbContextFactory = applicationDbContextFactory;
            _logger = logger;
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
                Roles = roles,
                Disabled = data.LockoutEnabled && data.LockoutEnd is not null && DateTimeOffset.UtcNow < data.LockoutEnd.Value.UtcDateTime
            };
        }

        private bool IsDisabled(ApplicationUser user)
        {
            return user.LockoutEnabled && user.LockoutEnd is not null &&
                   DateTimeOffset.UtcNow < user.LockoutEnd.Value.UtcDateTime;
        }
        public async Task ToggleUser(string userId, DateTimeOffset? lockedOutDeadline)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
            {
                return;
            }
            if (lockedOutDeadline is not null)
            {
                await _userManager.SetLockoutEnabledAsync(user, true);
            }

            var res = await _userManager.SetLockoutEndDateAsync(user, lockedOutDeadline);
            if (res.Succeeded)
            {
                _logger.LogInformation($"User {user.Id} is now {(lockedOutDeadline is null ? "unlocked" : "locked")}");
            }
            else
            {
                _logger.LogError($"Failed to set lockout for user {user.Id}");
            }
            
        }

        public async Task<bool> IsAdminUser(string userId)
        {
            return IsRoleAdmin(await _userManager.GetRolesAsync(new ApplicationUser() { Id = userId }));
        }

        public async Task<bool> IsAdminUser(ApplicationUser user)
        {
            return IsRoleAdmin(await _userManager.GetRolesAsync(user));
        }

        public async Task<bool> SetAdminUser(string userId, bool enableAdmin)
        {
            var user = await _userManager.FindByIdAsync(userId);
            IdentityResult res;
            if (enableAdmin)
            {
                res = await _userManager.AddToRoleAsync(user, Roles.ServerAdmin);
            }
            else
            {
                res = await _userManager.RemoveFromRoleAsync(user, Roles.ServerAdmin);
            }

            if (res.Succeeded)
            {
                _logger.LogInformation($"Successfully set admin status for user {user.Id}");
            }
            else
            {
                _logger.LogError($"Error setting admin status for user {user.Id}");
            }

            return res.Succeeded;
        }

        public async Task DeleteUserAndAssociatedData(ApplicationUser user)
        {
            var userId = user.Id;
            var files = await _storedFileRepository.GetFiles(new StoredFileRepository.FilesQuery()
            {
                UserIds = new[] { userId },
            });

            await Task.WhenAll(files.Select(file => _fileService.RemoveFile(file.Id, userId)));

            user = await _userManager.FindByIdAsync(userId);
            var res = await _userManager.DeleteAsync(user);
            if (res.Succeeded)
            {
                _logger.LogInformation($"User {user.Id} was successfully deleted");
            }
            else
            {
                _logger.LogError($"Failed to delete user {user.Id}");
            } 

            await _storeRepository.CleanUnreachableStores();
        }

        public bool IsRoleAdmin(IList<string> roles)
        {
            return roles.Contains(Roles.ServerAdmin, StringComparer.Ordinal);
        }


        public async Task<bool> IsUserTheOnlyOneAdmin(ApplicationUser user)
        {
            var isUserAdmin = await IsAdminUser(user);
            if (!isUserAdmin)
            {
                return false;
            }

            var adminUsers = await _userManager.GetUsersInRoleAsync(Roles.ServerAdmin);
            var enabledAdminUsers = adminUsers
                                        .Where(applicationUser => !IsDisabled(applicationUser))
                                        .Select(applicationUser => applicationUser.Id).ToList();

            return enabledAdminUsers.Count == 1 && enabledAdminUsers.Contains(user.Id);
        }
    }
}
