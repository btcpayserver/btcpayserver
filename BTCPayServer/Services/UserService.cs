#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Storage.Services;
using Microsoft.AspNetCore.Http;
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
        private readonly EventAggregator _eventAggregator;
        private readonly ApplicationDbContextFactory _applicationDbContextFactory;
        private readonly ILogger<UserService> _logger;

        public UserService(
            IServiceProvider serviceProvider,
            StoredFileRepository storedFileRepository,
            FileService fileService,
            EventAggregator eventAggregator,
            ApplicationDbContextFactory applicationDbContextFactory,
            ILogger<UserService> logger)
        {
            _serviceProvider = serviceProvider;
            _storedFileRepository = storedFileRepository;
            _fileService = fileService;
            _eventAggregator = eventAggregator;
            _applicationDbContextFactory = applicationDbContextFactory;
            _logger = logger;
        }

        public record ApplicationUserWithRoles(ApplicationUser User, string[] Roles);
        public async Task<List<ApplicationUserWithRoles>> GetUsersWithRoles()
        {
            await using var context = _applicationDbContextFactory.CreateContext();
            var res = await context.Users.Select(p =>
                        new
                        {
                            User = p,
                            Roles = p.UserRoles.Join(context.Roles, userRole => userRole.RoleId,
                            role => role.Id, (userRole, role) => role.Name).ToArray()
                        })
                .ToListAsync();
            return res.Select(p => new ApplicationUserWithRoles(p.User, (p.Roles ?? [])!)).ToList();
        }

        public static async Task<T> ForAPI<T>(
            ApplicationUser data,
            string?[] roles,
            CallbackGenerator callbackGenerator,
            UriResolver uriResolver,
            HttpRequest request) where T : ApplicationUserData, new()
        {
            var blob = data.GetBlob() ?? new UserBlob();
            return new T
            {
                Id = data.Id,
                Email = data.Email,
                EmailConfirmed = data.EmailConfirmed,
                RequiresEmailConfirmation = data.RequiresEmailConfirmation,
                Approved = data.Approved,
                RequiresApproval = data.RequiresApproval,
                Created = data.Created,
                Name = blob.Name,
                Roles = roles,
                Disabled = data.IsDisabled,
                ImageUrl = string.IsNullOrEmpty(blob.ImageUrl)
                    ? null
                    : await uriResolver.Resolve(request.GetAbsoluteRootUri(), UnresolvedUri.Create(blob.ImageUrl)),
                InvitationUrl = string.IsNullOrEmpty(blob.InvitationToken) ? null
                    : callbackGenerator.ForInvitation(data.Id, blob.InvitationToken, request)
            };
        }

        private static bool IsEmailConfirmed(ApplicationUser user)
        {
            return user.EmailConfirmed || !user.RequiresEmailConfirmation;
        }

        private static bool IsApproved(ApplicationUser user)
        {
            return user.Approved || !user.RequiresApproval;
        }
        
        public static bool TryCanLogin([NotNullWhen(true)] ApplicationUser? user, [MaybeNullWhen(true)] out string error)
        {
            error = null;
            if (user == null)
            {
                error = "Invalid login attempt.";
                return false;
            }
            if (!IsEmailConfirmed(user))
            {
                error = "You must have a confirmed email to log in.";
                return false;
            }
            if (!IsApproved(user))
            {
                error = "Your user account requires approval by an admin before you can log in.";
                return false;
            }
            if (user.IsDisabled)
            {
                error = "Your user account is currently disabled.";
                return false;
            }
            return true;
        }
        
        public async Task<bool> SetUserApproval(string userId, bool approved, string loginLink)
        {
            using var scope = _serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId);
            if (user is null || !user.RequiresApproval || user.Approved == approved)
            {
                return false;
            }
            
            user.Approved = approved;
            var succeeded = await userManager.UpdateAsync(user) is { Succeeded: true };
            if (succeeded)
            {
                _logger.LogInformation("User {Email} is now {Status}", user.Email, approved ? "approved" : "unapproved");
                _eventAggregator.Publish(new UserEvent.Approved(user, loginLink));
            }
            else
            {
                _logger.LogError("Failed to {Action} user {Email}", approved ? "approve" : "unapprove", user.Email);
            }

            return succeeded;
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
                _logger.LogInformation("User {Email} is now {Status}", user.Email, (lockedOutDeadline is null ? "unlocked" : "locked"));
            }
            else
            {
                _logger.LogError("Failed to set lockout for user {Email}", user.Email);
            }

            return res.Succeeded;
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
                _logger.LogInformation("Successfully set admin status for user {Email}", user.Email);
            }
            else
            {
                _logger.LogError("Error setting admin status for user {Email}", user.Email);
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
                _logger.LogInformation("User {Email} was successfully deleted", user.Email);
            }
            else
            {
                _logger.LogError("Failed to delete user {Email}", user.Email);
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
                                        .Where(applicationUser => !applicationUser.IsDisabled && IsApproved(applicationUser))
                                        .Select(applicationUser => applicationUser.Id).ToList();

            return enabledAdminUsers.Count == 1 && enabledAdminUsers.Contains(user.Id);
        }
    }
}
