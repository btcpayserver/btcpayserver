﻿#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayApp.CommonServer.Models;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using NicolasDorier.RateLimits;

namespace BTCPayServer.App.API;

public partial class AppApiController
{
    [AllowAnonymous]
    [HttpPost("register")]
    [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
    public async Task<IActionResult> Register(SignupRequest signup)
    {
        var policiesSettings = await settingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
        if (policiesSettings.LockSubscription)
            return this.CreateAPIError("unauthorized", "This instance does not allow public user registration");
            
        var errorMessage = "Invalid signup attempt.";
        if (ModelState.IsValid)
        {
            var user = new ApplicationUser
            {
                UserName = signup.Email,
                Email = signup.Email,
                RequiresEmailConfirmation = policiesSettings.RequiresConfirmedEmail,
                RequiresApproval = policiesSettings.RequiresUserApproval,
                Created = DateTimeOffset.UtcNow
            };
            var result = await userManager.CreateAsync(user, signup.Password);
            if (result.Succeeded)
            {
                eventAggregator.Publish(new UserRegisteredEvent
                {
                    RequestUri = Request.GetAbsoluteRootUri(),
                    User = user
                });

                var response = new SignupResult
                {
                    Email = user.Email,
                    RequiresConfirmedEmail = policiesSettings.RequiresConfirmedEmail && !user.EmailConfirmed,
                    RequiresUserApproval = policiesSettings.RequiresUserApproval && !user.Approved
                };
                return Ok(response);
            }
            errorMessage = result.ToString();
        }
        
        return this.CreateAPIError(null, errorMessage);
    }
    
    [AllowAnonymous]
    [HttpPost("login")]
    [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
    public async Task<Results<Ok<AccessTokenResponse>, EmptyHttpResult, ProblemHttpResult>> Login(LoginRequest login)
    {
        const string errorMessage = "Invalid login attempt.";
        if (ModelState.IsValid)
        {
            // Require the user to pass basic checks (approval, confirmed email, not disabled) before they can log on
            var user = await userManager.FindByEmailAsync(login.Email);
            if (!UserService.TryCanLogin(user, out var message))
            {
                return TypedResults.Problem(message, statusCode: 401);
            }

            signInManager.AuthenticationScheme = AuthenticationSchemes.GreenfieldBearer;
            var signInResult = await signInManager.PasswordSignInAsync(login.Email, login.Password, true, true);
            if (signInResult.RequiresTwoFactor)
            {
                if (!string.IsNullOrEmpty(login.TwoFactorCode))
                    signInResult = await signInManager.TwoFactorAuthenticatorSignInAsync(login.TwoFactorCode, true, true);
                else if (!string.IsNullOrEmpty(login.TwoFactorRecoveryCode))
                    signInResult = await signInManager.TwoFactorRecoveryCodeSignInAsync(login.TwoFactorRecoveryCode);
            }
            
            // TODO: Add FIDO and LNURL Auth

            return signInResult.Succeeded
                ? TypedResults.Empty
                : TypedResults.Problem(signInResult.ToString(), statusCode: 401);
        }

        return TypedResults.Problem(errorMessage, statusCode: 401);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
    public async Task<Results<Ok<AccessTokenResponse>, UnauthorizedHttpResult, SignInHttpResult, ChallengeHttpResult>> Refresh(RefreshRequest refresh)
    {
        const string scheme = AuthenticationSchemes.GreenfieldBearer;
        var authenticationTicket = bearerTokenOptions.Get(scheme).RefreshTokenProtector.Unprotect(refresh.RefreshToken);
        var expiresUtc = authenticationTicket?.Properties.ExpiresUtc;

        ApplicationUser? user = null;
        int num;
        if (expiresUtc.HasValue)
        {
            DateTimeOffset valueOrDefault = expiresUtc.GetValueOrDefault();
            num = timeProvider.GetUtcNow() >= valueOrDefault ? 1 : 0;
        }
        else
            num = 1;
        bool flag = num != 0;
        if (!flag)
        {
            signInManager.AuthenticationScheme = scheme;
            user = await signInManager.ValidateSecurityStampAsync(authenticationTicket?.Principal);
        }
        
        return user != null
            ? TypedResults.SignIn(await signInManager.CreateUserPrincipalAsync(user), authenticationScheme: scheme)
            : TypedResults.Challenge(authenticationSchemes: new[] { scheme });
    }

    [HttpPost("logout")]
    public async Task<IResult> Logout()
    {
        var user = await userManager.GetUserAsync(User);
        if (user != null)
        {
            await signInManager.SignOutAsync();
            return Results.Ok();
        }
        return Results.Unauthorized();
    }

    [HttpGet("user")]
    public async Task<Results<Ok<AppUserInfo>, NotFound>> UserInfo()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null) return TypedResults.NotFound();

        var userStores = await storeRepository.GetStoresByUserId(user.Id);
        return TypedResults.Ok(new AppUserInfo
        {
            UserId = user.Id,
            Email = await userManager.GetEmailAsync(user),
            Roles = await userManager.GetRolesAsync(user),
            Stores = (from store in userStores
                let userStore = store.UserStores.Find(us => us.ApplicationUserId == user.Id && us.StoreDataId == store.Id)!
                select new AppUserStoreInfo
                {
                    Id = store.Id,
                    Name = store.StoreName,
                    Archived = store.Archived,
                    RoleId = userStore.StoreRole.Id,
                    Permissions = userStore.StoreRole.Permissions
                }).ToList()
        });
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    [RateLimitsFilter(ZoneLimits.ForgotPassword, Scope = RateLimitsScope.RemoteAddress)]
    public async Task<IResult> ForgotPassword(ResetPasswordRequest resetRequest)
    {
        var user = await userManager.FindByEmailAsync(resetRequest.Email);
        if (UserService.TryCanLogin(user, out _))
        {
            eventAggregator.Publish(new UserPasswordResetRequestedEvent
            {
                User = user,
                RequestUri = Request.GetAbsoluteRootUri()
            });
        }
        return TypedResults.Ok();
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> SetPassword(ResetPasswordRequest resetRequest)
    {
        var user = await userManager.FindByEmailAsync(resetRequest.Email);
        if (!UserService.TryCanLogin(user, out _))
        {
            return Unauthorized(new GreenfieldAPIError(null, "Invalid account"));
        }

        IdentityResult result;
        try
        {
            result = await userManager.ResetPasswordAsync(user, resetRequest.ResetCode, resetRequest.NewPassword);
        }
        catch (FormatException)
        {
            result = IdentityResult.Failed(userManager.ErrorDescriber.InvalidToken());
        }
        return result.Succeeded ? Ok() : this.CreateAPIError(401, "unauthorized", result.ToString().Split(": ").Last());
    }
}