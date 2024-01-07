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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Services
{
    public class UserService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly StoredFileRepository _storedFileRepository;
        private readonly FileService _fileService;
        private readonly StoreRepository _storeRepository;
        private readonly ApplicationDbContextFactory _applicationDbContextFactory;
        private readonly ILogger<UserService> _logger;

        public UserService(
            IServiceProvider serviceProvider,
            StoredFileRepository storedFileRepository,
            FileService fileService,
            StoreRepository storeRepository,
            ApplicationDbContextFactory applicationDbContextFactory,
            ILogger<UserService> logger)
        {
            _serviceProvider = serviceProvider;
            _storedFileRepository = storedFileRepository;
            _fileService = fileService;
            _storeRepository = storeRepository;
            _applicationDbContextFactory = applicationDbContextFactory;
            _logger = logger;
        }

        public async Task<List<ApplicationUserData>> GetUsersWithRoles()
        {
            await using var context = _applicationDbContextFactory.CreateContext();
            return await (context.Users.Select(p => FromModel(p, p.UserRoles.Join(context.Roles, userRole => userRole.RoleId, role => role.Id,
               (userRole, role) => role.Name).ToArray()))).ToListAsync();
        }


        public static ApplicationUserData FromModel(ApplicationUser data, string?[] roles)
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
        public async Task<bool?> ToggleUser(string userId, DateTimeOffset? lockedOutDeadline)
        {
            using var scope = _serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId);
            if (user is null)
            {
                return null;
            }
            if (lockedOutDeadline is not null)
            {
                await userManager.SetLockoutEnabledAsync(user, true);
            }

            var res = await userManager.SetLockoutEndDateAsync(user, lockedOutDeadline);
            if (res.Succeeded)
            {
                _logger.LogInformation($"User {user.Id} is now {(lockedOutDeadline is null ? "unlocked" : "locked")}");
            }
            else
            {
                _logger.LogError($"Failed to set lockout for user {user.Id}");
            }

            return res.Succeeded;
        }

        public async Task<bool> IsAdminUser(string userId)
        {
            using var scope = _serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            return Roles.HasServerAdmin(await userManager.GetRolesAsync(new ApplicationUser() { Id = userId }));
        }

        public async Task<bool> IsAdminUser(ApplicationUser user)
        {
            using var scope = _serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            return Roles.HasServerAdmin(await userManager.GetRolesAsync(user));
        }

        public async Task<bool> SetAdminUser(string userId, bool enableAdmin)
        {
            using var scope = _serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId);
            if (user is null)
                return false;
            IdentityResult res;
            if (enableAdmin)
            {
                res = await userManager.AddToRoleAsync(user, Roles.ServerAdmin);
            }
            else
            {
                res = await userManager.RemoveFromRoleAsync(user, Roles.ServerAdmin);
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
            using var scope = _serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var userId = user.Id;
            var files = await _storedFileRepository.GetFiles(new StoredFileRepository.FilesQuery()
            {
                UserIds = new[] { userId },
            });

            await Task.WhenAll(files.Select(file => _fileService.RemoveFile(file.Id, userId)));

            user = (await userManager.FindByIdAsync(userId))!;
            if (user is null)
                return;
            var res = await userManager.DeleteAsync(user);
            if (res.Succeeded)
            {
                _logger.LogInformation($"User {user.Id} was successfully deleted");
            }
            else
            {
                _logger.LogError($"Failed to delete user {user.Id}");
            }
        }


        public async Task<bool> IsUserTheOnlyOneAdmin(ApplicationUser user)
        {
            using var scope = _serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = await userManager.GetRolesAsync(user);
            if (!Roles.HasServerAdmin(roles))
            {
                return false;
            }
            var adminUsers = await userManager.GetUsersInRoleAsync(Roles.ServerAdmin);
            var enabledAdminUsers = adminUsers
                                        .Where(applicationUser => !IsDisabled(applicationUser))
                                        .Select(applicationUser => applicationUser.Id).ToList();

            return enabledAdminUsers.Count == 1 && enabledAdminUsers.Contains(user.Id);
        }
    }
}
