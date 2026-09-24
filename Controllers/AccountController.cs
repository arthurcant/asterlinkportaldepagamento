using System.Security.Claims;
using asterlinkportaldepagamento.Data;
using asterlinkportaldepagamento.Models;
using asterlinkportaldepagamento.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace asterlinkportaldepagamento.Controllers;

[Route("conta")]
public sealed class AccountController(IUserRepository users, IPlanRepository plans, IPasswordService passwords) : Controller
{
    [HttpGet("")]
    public IActionResult Index(string mode = "login")
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(nameof(Dashboard));
        }

        return View(new AuthViewModel { Mode = mode == "register" ? "register" : "login" });
    }

    [HttpPost("entrar"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Login([Bind(Prefix = "Login")] LoginInputModel input, CancellationToken cancellationToken)
    {
        var x = HttpContext.Connection.RemoteIpAddress;


        var model = new AuthViewModel
        {
            Mode = "login",
            Login = input
        };

        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        var user = await users.FindByLoginAsync(input.Identifier, cancellationToken);

        if (user is null || !passwords.Verify(input.Password, user.PasswordHash))
        {
            ModelState.AddModelError(string.Empty, "Login, e-mail ou senha inválidos.");

            return View("Index", model);
        }

        await SignInAsync(user);

        return RedirectToAction(nameof(Dashboard));
    }

    [HttpPost("cadastrar"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(
        [Bind(Prefix = "Register")] RegisterInputModel input,
        CancellationToken cancellationToken)
    {
        var model = new AuthViewModel
        {
            Mode = "register",
            Register = input
        };

        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        try
        {
            var id = await users.CreateAsync(input.Name, input.Username, input.Email, passwords.Hash(input.Password), cancellationToken);
            await SignInAsync(new User
            {
                Id = id,
                Name = input.Name.Trim(),
                Username = input.Username.Trim().ToLowerInvariant(),
                Email = input.Email.Trim().ToLowerInvariant()
            });

            return RedirectToAction(nameof(Dashboard));
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            ModelState.AddModelError(string.Empty, "Este login ou e-mail já está cadastrado.");

            return View("Index", model);
        }
    }

    [Authorize, HttpGet("painel")]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
    => View(new PlansViewModel { Plans = await plans.GetActiveAsync(cancellationToken) });

    [Authorize, HttpPost("sair"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return RedirectToAction(nameof(Index));
    }

    private async Task SignInAsync(User user)
    {
        var identity = new ClaimsIdentity(
            new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email)
        },
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }
}
