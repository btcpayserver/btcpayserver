#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayApp.CommonServer;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Common;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer;
using NicolasDorier.RateLimits;

namespace BTCPayServer.App;

[ApiController]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Bearer)]
[Route("btcpayapp")]
public class BtcPayAppController(
    BtcPayAppService appService,
    APIKeyRepository apiKeyRepository,
    StoreRepository storeRepository,
    BTCPayNetworkProvider btcPayNetworkProvider,
    IExplorerClientProvider explorerClientProvider,
    EventAggregator eventAggregator,
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider,
    PaymentMethodHandlerDictionary handlers,
    IFileService fileService,
    ISettingsRepository settingsRepository,
    IOptionsMonitor<BearerTokenOptions> bearerTokenOptions)
    : Controller
{
    [AllowAnonymous]
    [HttpGet("instance")]
    public async Task<Results<Ok<AppInstanceInfo>, NotFound>> Instance()
    {
        var serverSettings = await settingsRepository.GetSettingAsync<ServerSettings>() ?? new ServerSettings();
        var policiesSettings = await settingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
        var themeSettings = await settingsRepository.GetSettingAsync<ThemeSettings>() ?? new ThemeSettings();

        return TypedResults.Ok(new AppInstanceInfo
        {
            BaseUrl = Request.GetAbsoluteRoot(),
            ServerName = serverSettings.ServerName,
            ContactUrl = serverSettings.ContactUrl,
            RegistrationEnabled = policiesSettings.EnableRegistration,
            CustomThemeExtension = themeSettings.CustomTheme ? themeSettings.CustomThemeExtension.ToString() : null,
            CustomThemeCssUrl = themeSettings.CustomTheme && !string.IsNullOrEmpty(themeSettings.CustomThemeFileId)
                ? await fileService.GetFileUrl(Request.GetAbsoluteRootUri(), themeSettings.CustomThemeFileId)
                : null,
            LogoUrl = !string.IsNullOrEmpty(themeSettings.LogoFileId)
                ? await fileService.GetFileUrl(Request.GetAbsoluteRootUri(), themeSettings.LogoFileId)
                : null
        });
    }
    
    [AllowAnonymous]
    [HttpPost("register")]
    [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
    public async Task<Results<Ok<SignupResult>, ValidationProblem, ProblemHttpResult>> Register(SignupRequest signup)
    {
        var policiesSettings = await settingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
        if (policiesSettings.LockSubscription)
            return TypedResults.Problem("This instance does not allow public user registration", statusCode: 406);
            
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
                return TypedResults.Ok(response);
            }
            errorMessage = result.ToString();
        }
        return TypedResults.Problem(errorMessage, statusCode: 400);
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

            signInManager.AuthenticationScheme = AuthenticationSchemes.Bearer;
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
        const string scheme = AuthenticationSchemes.Bearer;
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
    public async Task<IResult> SetPassword(ResetPasswordRequest resetRequest)
    {
        var user = await userManager.FindByEmailAsync(resetRequest.Email);
        if (!UserService.TryCanLogin(user, out _))
        {
            return TypedResults.Problem("Invalid account", statusCode: 401);
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
        return result.Succeeded ? TypedResults.Ok() : TypedResults.Problem(result.ToString().Split(": ").Last(), statusCode: 401);
    }

    [HttpGet("pair/{code}")]
    public async Task<IActionResult> StartPair(string code)
    {
        var res = appService.ConsumePairingCode(code);
        if (res is null)
        {
            return Unauthorized();
        }

        StoreData? store = null;
        if (res.StoreId is not null)
        {
            store = await storeRepository.FindStore(res.StoreId, res.UserId);
            if (store is null)
            {
                return NotFound();
            }
        }
        
        var key = new APIKeyData
        {
            Id = Encoders.Hex.EncodeData(RandomUtils.GetBytes(20)),
            Type = APIKeyType.Permanent,
            UserId = res.UserId,
            Label = "BTCPay App Pairing"
        };
        key.SetBlob(new APIKeyBlob {Permissions = [Policies.Unrestricted] });
        await apiKeyRepository.CreateKey(key);
        
        var onchain = store?.GetDerivationSchemeSettings(handlers, "BTC");
        string? onchainSeed = null;
        if (onchain is not null)
        {
            var explorerClient = explorerClientProvider.GetExplorerClient("BTC");
            onchainSeed = await GetSeed(explorerClient, onchain);
        }

        var nBitcoinNetwork = btcPayNetworkProvider.GetNetwork<BTCPayNetwork>("BTC").NBitcoinNetwork;
        return Ok(new PairSuccessResult
        {
            Key = key.Id,
            StoreId = store?.Id,
            UserId = res.UserId,
            ExistingWallet = onchain?.AccountDerivation?.GetExtPubKeys()?.FirstOrDefault()?.ToString(nBitcoinNetwork),
            ExistingWalletSeed = onchainSeed,
            Network = nBitcoinNetwork.Name
        });
    }

    private async Task<string?> GetSeed(ExplorerClient client, DerivationSchemeSettings derivation)
    {
        return derivation.IsHotWallet &&
               await client.GetMetadataAsync<string>(derivation.AccountDerivation, WellknownMetadataKeys.Mnemonic) is
                   { } seed &&
               !string.IsNullOrEmpty(seed)
            ? seed
            : null;
    }
}
