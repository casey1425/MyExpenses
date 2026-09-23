using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;

namespace MyExpenses;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/account/google-login", StartGoogleLogin).AllowAnonymous();
        endpoints.MapGet("/account/google-callback", CompleteGoogleLoginAsync).AllowAnonymous();
        endpoints.MapPost("/account/logout", LogoutAsync).RequireAuthorization();
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
        return Results.Challenge(properties, [GoogleDefaults.AuthenticationScheme]);
    }

    private static async Task<IResult> CompleteGoogleLoginAsync(
        string? returnUrl,
        string? remoteError,
        IConfiguration configuration,
        HttpContext httpContext,
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager)
    {
        var destination = SafeReturnUrl(returnUrl);
        if (!string.IsNullOrEmpty(remoteError))
            return Results.LocalRedirect(LoginUrl("Google 로그인이 취소되었거나 실패했습니다.", destination));

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
            return Results.LocalRedirect(LoginUrl("Google 로그인 정보를 확인하지 못했습니다. 다시 시도해 주세요.", destination));

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        var allowedEmail = configuration["Authentication:Google:AllowedEmail"]?.Trim();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(allowedEmail) ||
            !string.Equals(email, allowedEmail, StringComparison.OrdinalIgnoreCase))
        {
            await httpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return Results.LocalRedirect(LoginUrl("이 앱에 허용된 Google 계정이 아닙니다.", destination));
        }

        var externalResult = await signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: true, bypassTwoFactor: true);
        if (externalResult.Succeeded)
            return Results.LocalRedirect(destination);

        var user = await userManager.FindByEmailAsync(email);
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

        await signInManager.SignInAsync(user, isPersistent: true, info.LoginProvider);
        return Results.LocalRedirect(destination);
    }

    private static async Task<IResult> LogoutAsync(SignInManager<IdentityUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return Results.LocalRedirect("/account/login");
    }

    private static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith('/') &&
        !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
            ? returnUrl
            : "/";

    private static string LoginUrl(string message, string returnUrl) =>
        $"/account/login?error={Uri.EscapeDataString(message)}&returnUrl={Uri.EscapeDataString(returnUrl)}";

    private static bool GoogleIsConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"]) &&
        !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]) &&
        !string.IsNullOrWhiteSpace(configuration["Authentication:Google:AllowedEmail"]);
}
