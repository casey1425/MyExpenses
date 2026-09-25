using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using MyExpenses.Data;

namespace MyExpenses;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/account/google-login", StartGoogleLogin).AllowAnonymous();
        endpoints.MapGet("/account/google-callback", CompleteGoogleLoginAsync).AllowAnonymous();
        endpoints.MapPost("/account/logout", LogoutAsync).RequireAuthorization();
        endpoints.MapPost("/account/delete", DeleteAccountAsync).RequireAuthorization();
        return endpoints;
    }

    private static IResult StartGoogleLogin(
        string? returnUrl,
        IConfiguration configuration,
        SignInManager<IdentityUser> signInManager)
    {
        var destination = SafeReturnUrl(returnUrl);
        if (!GoogleIsConfigured(configuration))
            return Results.LocalRedirect(LoginUrl("Google 로그인 설정이 필요합니다. README의 설정 방법을 확인해 주세요.", destination));

        var callbackUrl = $"/account/google-callback?returnUrl={Uri.EscapeDataString(destination)}";
        var properties = signInManager.ConfigureExternalAuthenticationProperties(
            GoogleDefaults.AuthenticationScheme, callbackUrl);
        properties.SetParameter(GoogleChallengeProperties.PromptParameterKey, "select_account");
        return Results.Challenge(properties, [GoogleDefaults.AuthenticationScheme]);
    }

    private static async Task<IResult> CompleteGoogleLoginAsync(
        string? returnUrl,
        string? remoteError,
        HttpContext httpContext,
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        UserDataProvisioner userDataProvisioner)
    {
        var destination = SafeReturnUrl(returnUrl);
        if (!string.IsNullOrEmpty(remoteError))
            return Results.LocalRedirect(LoginUrl("Google 로그인이 취소되었거나 실패했습니다.", destination));

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
            return Results.LocalRedirect(LoginUrl("Google 로그인 정보를 확인하지 못했습니다. 다시 시도해 주세요.", destination));

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            await httpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return Results.LocalRedirect(LoginUrl("Google 계정의 이메일을 확인하지 못했습니다.", destination));
        }

        var user = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey)
            ?? await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };
            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
                return Results.LocalRedirect(LoginUrl("로그인 계정을 저장하지 못했습니다.", destination));
        }

        var existingLogins = await userManager.GetLoginsAsync(user);
        if (!existingLogins.Any(login => login.LoginProvider == info.LoginProvider &&
                                        login.ProviderKey == info.ProviderKey))
        {
            var linkResult = await userManager.AddLoginAsync(user, info);
            if (!linkResult.Succeeded)
                return Results.LocalRedirect(LoginUrl("Google 계정을 연결하지 못했습니다.", destination));
        }

        var needsOnboarding = await userDataProvisioner.EnsureUserAsync(user.Id);
        await signInManager.SignInAsync(user, isPersistent: true, info.LoginProvider);
        return Results.LocalRedirect(needsOnboarding ? "/welcome" : destination);
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        IAntiforgery antiforgery,
        SignInManager<IdentityUser> signInManager)
    {
        if (!await HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
            return Results.BadRequest("잘못된 요청입니다.");

        await signInManager.SignOutAsync();
        return Results.LocalRedirect("/account/login");
    }

    private static async Task<IResult> DeleteAccountAsync(
        HttpContext httpContext,
        IAntiforgery antiforgery,
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        UserDataDeletionService userDataDeletionService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!await HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
            return Results.BadRequest("잘못된 요청입니다.");

        var form = await httpContext.Request.ReadFormAsync(cancellationToken);
        var confirmed = string.Equals(form["confirmation"], "계정 삭제", StringComparison.Ordinal) &&
                        string.Equals(form["understood"], "true", StringComparison.Ordinal);
        if (!confirmed)
            return Results.LocalRedirect(AccountUrl("안내 문구를 정확히 입력하고 확인란을 선택해 주세요."));

        var ownerId = userManager.GetUserId(httpContext.User);
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        var user = await userManager.FindByIdAsync(ownerId);
        if (user is null)
        {
            await signInManager.SignOutAsync();
            return Results.LocalRedirect("/account/deleted");
        }

        await userDataDeletionService.DeleteAsync(ownerId, cancellationToken);
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            var logger = loggerFactory.CreateLogger(typeof(AccountEndpoints));
            logger.LogError("Identity 계정 삭제에 실패했습니다. UserId: {UserId}, Errors: {Errors}",
                ownerId, string.Join(", ", result.Errors.Select(error => error.Code)));
            return Results.LocalRedirect(AccountUrl("지출 데이터는 삭제되었지만 로그인 계정 정리를 완료하지 못했습니다. 다시 시도해 주세요."));
        }

        await signInManager.SignOutAsync();
        return Results.LocalRedirect("/account/deleted");
    }

    private static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith('/') &&
        !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
            ? returnUrl
            : "/";

    private static string LoginUrl(string message, string returnUrl) =>
        $"/account/login?error={Uri.EscapeDataString(message)}&returnUrl={Uri.EscapeDataString(returnUrl)}";

    private static string AccountUrl(string message) =>
        $"/account?error={Uri.EscapeDataString(message)}";

    private static async Task<bool> HasValidAntiforgeryTokenAsync(
        HttpContext httpContext,
        IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private static bool GoogleIsConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"]) &&
        !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]);
}
