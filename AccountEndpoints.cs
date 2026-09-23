using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MyExpenses;

public static class AccountEndpoints
{
    private static readonly SemaphoreSlim RegistrationLock = new(1, 1);

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/account/login", LoginAsync).AllowAnonymous();
        endpoints.MapPost("/account/register", RegisterAsync).AllowAnonymous();
        endpoints.MapPost("/account/logout", LogoutAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        [FromForm] LoginForm form,
        SignInManager<IdentityUser> signInManager)
    {
        var returnUrl = SafeReturnUrl(form.ReturnUrl);
        var result = await signInManager.PasswordSignInAsync(
            form.Email.Trim(), form.Password, form.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
            return Results.LocalRedirect(returnUrl);

        var message = result.IsLockedOut
            ? "로그인 시도가 여러 번 실패해 잠시 잠겼습니다. 5분 후 다시 시도해 주세요."
            : "이메일 또는 비밀번호가 올바르지 않습니다.";
        return Results.LocalRedirect(LoginUrl(message, returnUrl));
    }

    private static async Task<IResult> RegisterAsync(
        [FromForm] RegisterForm form,
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager)
    {
        await RegistrationLock.WaitAsync();
        try
        {
            if (await userManager.Users.AnyAsync())
                return Results.LocalRedirect(LoginUrl("이미 관리자 계정이 만들어져 있습니다.", "/"));

            if (!string.Equals(form.Password, form.ConfirmPassword, StringComparison.Ordinal))
                return Results.LocalRedirect(RegisterUrl("비밀번호 확인이 일치하지 않습니다."));

            var email = form.Email.Trim();
            var user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };
            var result = await userManager.CreateAsync(user, form.Password);
            if (!result.Succeeded)
            {
                var message = string.Join(" ", result.Errors.Select(error => TranslateIdentityError(error.Code)));
                return Results.LocalRedirect(RegisterUrl(message));
            }

            await signInManager.SignInAsync(user, isPersistent: false);
            return Results.LocalRedirect("/");
        }
        finally
        {
            RegistrationLock.Release();
        }
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

    private static string RegisterUrl(string message) =>
        $"/account/register?error={Uri.EscapeDataString(message)}";

    private static string TranslateIdentityError(string code) => code switch
    {
        "InvalidEmail" => "올바른 이메일 주소를 입력해 주세요.",
        "PasswordTooShort" => "비밀번호는 10자 이상이어야 합니다.",
        "PasswordRequiresDigit" => "비밀번호에 숫자를 하나 이상 포함해 주세요.",
        "PasswordRequiresLower" => "비밀번호에 영문 소문자를 하나 이상 포함해 주세요.",
        "PasswordRequiresUpper" => "비밀번호에 영문 대문자를 하나 이상 포함해 주세요.",
        "DuplicateEmail" or "DuplicateUserName" => "이미 사용 중인 이메일입니다.",
        _ => "계정을 만들 수 없습니다. 입력 내용을 확인해 주세요."
    };

    public sealed class LoginForm
    {
        public string Email { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public bool RememberMe { get; init; }
        public string? ReturnUrl { get; init; }
    }

    public sealed class RegisterForm
    {
        public string Email { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public string ConfirmPassword { get; init; } = string.Empty;
    }
}
