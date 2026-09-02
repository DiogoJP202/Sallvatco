using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Sallvat.Application.Accounts;
using Sallvat.Web.Configuration;
using Sallvat.Web.Email;
using Sallvat.Web.Models.Account;
using Sallvat.Web.Security;

namespace Sallvat.Web.Controllers;

[Route("conta")]
public sealed class AccountController(
    IAccountService accountService,
    IEmailSender emailSender,
    IRecoveryRequestLimiter recoveryRequestLimiter,
    IOptions<AccountLinkOptions> accountLinkOptions) : Controller
{
    private readonly Uri publicOrigin = new(
        accountLinkOptions.Value.PublicOrigin,
        UriKind.Absolute);

    [AllowAnonymous]
    [HttpGet("criar")]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated is true)
        {
            return RedirectToAction(nameof(Index));
        }

        return View(new RegisterViewModel());
    }

    [AllowAnonymous]
    [HttpPost("criar")]
    [EnableRateLimiting(RateLimitPolicyNames.Registration)]
    public async Task<IActionResult> Register(
        RegisterViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await accountService.RegisterAsync(
            new RegisterAccountCommand(
                model.Name,
                model.Email,
                model.Phone,
                model.Password),
            cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return View(model);
        }

        if (result.EmailChallenge is not null)
        {
            await TrySendConfirmationAsync(
                result.EmailChallenge,
                cancellationToken);
        }

        return RedirectToAction(nameof(CheckEmail));
    }

    [AllowAnonymous]
    [HttpGet("verifique-seu-email")]
    public IActionResult CheckEmail() => View();

    [AllowAnonymous]
    [HttpGet("confirmar-email")]
    public async Task<IActionResult> ConfirmEmail(
        Guid userId,
        string? code,
        CancellationToken cancellationToken)
    {
        var token = DecodeToken(code);
        var succeeded = token is not null
            && await accountService.ConfirmEmailAsync(
                userId,
                token,
                cancellationToken);

        return View(succeeded);
    }

    [AllowAnonymous]
    [HttpGet("reenviar-confirmacao")]
    public IActionResult ResendConfirmation() => View(new EmailViewModel());

    [AllowAnonymous]
    [HttpPost("reenviar-confirmacao")]
    [EnableRateLimiting(RateLimitPolicyNames.Recovery)]
    public async Task<IActionResult> ResendConfirmation(
        EmailViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (recoveryRequestLimiter.TryAcquire(model.Email))
        {
            var challenge = await accountService
                .CreateEmailConfirmationChallengeAsync(
                    model.Email,
                    cancellationToken);
            if (challenge is not null)
            {
                await TrySendConfirmationAsync(challenge, cancellationToken);
            }
        }

        return RedirectToAction(nameof(CheckEmail));
    }

    [AllowAnonymous]
    [HttpGet("entrar")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated is true)
        {
            return RedirectToAction(nameof(Index));
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost("entrar")]
    [EnableRateLimiting(RateLimitPolicyNames.Login)]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await accountService.SignInAsync(
            model.Email,
            model.Password,
            model.RememberMe);
        if (result == AccountSignInStatus.Succeeded)
        {
            return Url.IsLocalUrl(model.ReturnUrl)
                ? LocalRedirect(model.ReturnUrl)
                : RedirectToAction(nameof(Index));
        }

        ModelState.AddModelError(
            string.Empty,
            result == AccountSignInStatus.LockedOut
                ? "Acesso temporariamente bloqueado. Aguarde 15 minutos e tente novamente."
                : "Não foi possível entrar com os dados informados.");

        return View(model);
    }

    [Authorize]
    [HttpPost("sair")]
    public async Task<IActionResult> Logout()
    {
        await accountService.SignOutAsync();

        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous]
    [HttpGet("esqueci-minha-senha")]
    public IActionResult ForgotPassword() => View(new EmailViewModel());

    [AllowAnonymous]
    [HttpPost("esqueci-minha-senha")]
    [EnableRateLimiting(RateLimitPolicyNames.Recovery)]
    public async Task<IActionResult> ForgotPassword(
        EmailViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (recoveryRequestLimiter.TryAcquire(model.Email))
        {
            var challenge = await accountService
                .CreatePasswordResetChallengeAsync(
                    model.Email,
                    cancellationToken);
            if (challenge is not null)
            {
                await TrySendPasswordResetAsync(
                    challenge,
                    cancellationToken);
            }
        }

        return RedirectToAction(nameof(RecoveryRequested));
    }

    [AllowAnonymous]
    [HttpGet("recuperacao-enviada")]
    public IActionResult RecoveryRequested() => View();

    [AllowAnonymous]
    [HttpGet("redefinir-senha")]
    public IActionResult ResetPassword(Guid userId, string? code)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(code))
        {
            return View("InvalidRecoveryLink");
        }

        return View(new ResetPasswordViewModel
        {
            UserId = userId,
            Code = code,
        });
    }

    [AllowAnonymous]
    [HttpPost("redefinir-senha")]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var token = DecodeToken(model.Code);
        if (token is null)
        {
            return View("InvalidRecoveryLink");
        }

        var result = await accountService.ResetPasswordAsync(
            model.UserId,
            token,
            model.Password,
            cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return View(model);
        }

        return RedirectToAction(nameof(PasswordReset));
    }

    [AllowAnonymous]
    [HttpGet("senha-redefinida")]
    public IActionResult PasswordReset() => View();

    [Authorize]
    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var profile = await GetCurrentProfileAsync(cancellationToken);

        return profile is null ? NotFound() : View(ToViewModel(profile));
    }

    [Authorize]
    [HttpPost("perfil")]
    public async Task<IActionResult> UpdateProfile(
        ProfileViewModel model,
        CancellationToken cancellationToken)
    {
        var currentProfile = await GetCurrentProfileAsync(cancellationToken);
        if (currentProfile is null)
        {
            return NotFound();
        }

        model.Email = currentProfile.Email;
        model.EmailConfirmed = currentProfile.EmailConfirmed;
        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        var result = await accountService.UpdateProfileAsync(
            currentProfile.UserId,
            model.Name,
            model.Phone,
            cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return View("Index", model);
        }

        TempData["StatusMessage"] = "Perfil atualizado.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize]
    [HttpGet("seguranca")]
    public IActionResult ChangePassword() =>
        View(new ChangePasswordViewModel());

    [Authorize]
    [HttpPost("seguranca")]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await accountService.ChangePasswordAsync(
            CurrentUserId(),
            model.CurrentPassword,
            model.NewPassword,
            cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return View(model);
        }

        TempData["StatusMessage"] = "Senha alterada com segurança.";
        return RedirectToAction(nameof(ChangePassword));
    }

    [Authorize]
    [HttpGet("pedidos")]
    public IActionResult Orders() => View();

    [AllowAnonymous]
    [HttpGet("acesso-negado")]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    private async Task<AccountProfile?> GetCurrentProfileAsync(
        CancellationToken cancellationToken) =>
        await accountService.GetProfileAsync(
            CurrentUserId(),
            cancellationToken);

    private Guid CurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new InvalidOperationException(
                "Authenticated user has no valid identifier.");
    }

    private async Task TrySendConfirmationAsync(
        AccountEmailChallenge challenge,
        CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendEmailConfirmationAsync(
                challenge.Email,
                BuildAccountLink(
                    nameof(ConfirmEmail),
                    challenge.UserId,
                    challenge.Token),
                cancellationToken);
        }
        catch (AccountEmailDeliveryUnavailableException)
        {
            // A resposta permanece genérica. O provedor será definido por PBD-010.
        }
    }

    private async Task TrySendPasswordResetAsync(
        AccountEmailChallenge challenge,
        CancellationToken cancellationToken)
    {
        try
        {
            await emailSender.SendPasswordResetAsync(
                challenge.Email,
                BuildAccountLink(
                    nameof(ResetPassword),
                    challenge.UserId,
                    challenge.Token),
                cancellationToken);
        }
        catch (AccountEmailDeliveryUnavailableException)
        {
            // A resposta permanece genérica. O provedor será definido por PBD-010.
        }
    }

    private Uri BuildAccountLink(
        string action,
        Guid userId,
        string token)
    {
        var relativeUrl = Url.Action(
            action,
            "Account",
            new
            {
                userId,
                code = EncodeToken(token),
            }) ?? throw new InvalidOperationException(
                "Could not generate account link.");

        return new Uri(publicOrigin, relativeUrl);
    }

    private static string EncodeToken(string token) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private static string? DecodeToken(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(
                WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private void AddErrors(IEnumerable<string> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }

    private static ProfileViewModel ToViewModel(AccountProfile profile) =>
        new()
        {
            Name = profile.Name,
            Email = profile.Email,
            Phone = profile.Phone,
            EmailConfirmed = profile.EmailConfirmed,
        };
}
