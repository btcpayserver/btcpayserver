#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Storage.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
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
        private readonly BTCPayServerSecurityStampValidator.DisabledUsers _disabledUsers;
        private readonly IEnumerable<LoginExtension> _loginExtensions;
        private readonly ILogger<UserService> _logger;

        public UserService(
            IServiceProvider serviceProvider,
            StoredFileRepository storedFileRepository,
            FileService fileService,
            EventAggregator eventAggregator,
            ApplicationDbContextFactory applicationDbContextFactory,
            BTCPayServerSecurityStampValidator.DisabledUsers disabledUsers,
            IEnumerable<LoginExtension> loginExtensions,
            ILogger<UserService> logger)
        {
            _serviceProvider = serviceProvider;
            _storedFileRepository = storedFileRepository;
            _fileService = fileService;
            _eventAggregator = eventAggregator;
            _applicationDbContextFactory = applicationDbContextFactory;
            _disabledUsers = disabledUsers;
            _loginExtensions = loginExtensions;
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
                    : callbackGenerator.ForInvitation(data.Id, blob.InvitationToken)
            };
        }

        public class LoginFailure
        {
            public LoginFailure(LocalizedString text)
            {
                ArgumentNullException.ThrowIfNull(text);
                Text = text;
            }
            public LoginFailure(LocalizedString text, LocalizedHtmlString html) : this(text)
            {
                Html = html;
            }

            public LocalizedString Text { get; }
            public LocalizedHtmlString? Html { get; }
            public override string ToString() => Html?.ToString() ?? Text?.ToString() ?? "";
        }

        public abstract class LoginExtension
        {
            public abstract Task Check(CanLoginContext context);
        }

        public class CanLoginContext(
            ApplicationUser? user,
            IStringLocalizer? stringLocalizer = null,
            IViewLocalizer? viewLocalizer = null,
            RequestBaseUrl? baseUrl = null)
        {
            public CanLoginContext Clone(ApplicationUser? user) => new(user, stringLocalizer, viewLocalizer, BaseUrl);
            public IStringLocalizer StringLocalizer { get; } = stringLocalizer ?? NullStringLocalizer.Instance;
            public IViewLocalizer ViewLocalizer { get; } = viewLocalizer ?? NullViewLocalizer.Instance;
            public RequestBaseUrl? BaseUrl { get; } = baseUrl;
            internal readonly ApplicationUser? _user = user;
            public ApplicationUser User => _user ?? throw new InvalidOperationException("User is not set");
            public List<LoginFailure> Failures { get; } = new();
            /// <summary>
            /// A redirect URL to redirect the user if login failed.
            /// </summary>
            public string? FailedRedirectUrl { get; set; }
        }

        public async Task<bool> CanLogin(CanLoginContext context)
        {
            if (context._user is null)
            {
                context.Failures.Add(new LoginFailure(context.StringLocalizer["User not found or invalid password"]));
                return false;
            }

            foreach (var loginExtension in _loginExtensions)
            {
                await loginExtension.Check(context);
            }
            return !context.Failures.Any();
        }

        public class DefaultLoginExtension : LoginExtension
        {
            public override Task Check(CanLoginContext context)
            {
                if (context.User is { EmailConfirmed: false, RequiresEmailConfirmation: true })
                    context.Failures.Add(new(context.StringLocalizer["You must have a confirmed email to log in."]));
                else if (context.User.PasswordHash is null)
                    context.Failures.Add(new(context.StringLocalizer["Your user account has no password set."]));

                if (context.User is { Approved: false, RequiresApproval: true })
                    context.Failures.Add(new(context.StringLocalizer["Your user account requires approval by an admin before you can log in."]));
                if (context.User is { IsDisabled: true })
                    context.Failures.Add(new(context.StringLocalizer["Your user account is currently disabled."]));
                return Task.CompletedTask;
            }
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

        public record SetDisabledResult
        {
            public record Unchanged : SetDisabledResult;
            public record Success : SetDisabledResult;
            public record Error(IdentityError[] Errors) : SetDisabledResult;
        }

        public async Task<SetDisabledResult> SetDisabled(string userId, bool disabled)
        {
            using var scope = _serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId);
            if (user is null || disabled == user.IsDisabled)
                return new SetDisabledResult.Unchanged();
            if (!user.LockoutEnabled)
                await userManager.SetLockoutEnabledAsync(user, true);

            var lockedOutDeadline = disabled ? DateTimeOffset.MaxValue : (DateTimeOffset?)null;
            var res = await userManager.SetLockoutEndDateAsync(user, lockedOutDeadline);
            // Without this, the user won't be logged out automatically when his authentication ticket expires
            if (disabled)
            {
                await userManager.UpdateSecurityStampAsync(user);
                _disabledUsers.Add(userId);
            }
            else
            {
                _disabledUsers.Remove(userId);
            }
            return res.Succeeded ? new SetDisabledResult.Success() : new SetDisabledResult.Error(res.Errors.ToArray());
        }

        [Obsolete("Use SetDisabled instead")]
        public async Task<bool?> ToggleUser(string userId, DateTimeOffset? lockedOutDeadline)
            => await SetDisabled(userId, lockedOutDeadline is not null) is not SetDisabledResult.Error;

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
                _eventAggregator.Publish(new UserEvent.Deleted(user));
            }
        }

        public async Task<bool> IsUserTheOnlyOneAdmin(CanLoginContext canLoginContext)
        {
            using var scope = _serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = await userManager.GetRolesAsync(canLoginContext.User);
            if (!Roles.HasServerAdmin(roles))
            {
                return false;
            }
            var adminUsers = await userManager.GetUsersInRoleAsync(Roles.ServerAdmin);
            var enabledAdminUsers = new List<string>();
            foreach (var admin in adminUsers)
            {
                var loginContext = canLoginContext.Clone(admin);
                if (await CanLogin(loginContext))
                    enabledAdminUsers.Add(admin.Id);
            }

            return enabledAdminUsers.Count == 1 && enabledAdminUsers.Contains(canLoginContext.User.Id);
        }
    }
}
