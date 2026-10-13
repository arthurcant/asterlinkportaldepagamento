using System.Security.Claims;
using asterlinkportaldepagamento.Controllers;
using asterlinkportaldepagamento.Data;
using asterlinkportaldepagamento.Models;
using asterlinkportaldepagamento.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;

internal static class AccountLoginTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var authentication = new TestAuthenticationService();
        var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(authentication)
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        var controller = new AccountController(new TestUsers(), null!, new TestPasswords())
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            Url = new TestUrls()
        };

        var result = await controller.Login(
            new LoginInputModel { Identifier = "visitor", Password = "correct-password" },
            default);

        check(authentication.SignedIn, "valid login creates authentication session");
        check(result is RedirectResult { Url: "/rede/entrada?returnUrl=%2Fconta%2Fpainel" },
            "valid login routes through device identification before dashboard");
    }

    private sealed class TestUrls : IUrlHelper
    {
        public ActionContext ActionContext => new();

        public string? Action(UrlActionContext actionContext) => "/conta/painel";

        public string? Content(string? contentPath) => contentPath;

        public bool IsLocalUrl(string? url) => url?.StartsWith('/') == true;

        public string? Link(string? routeName, object? values) => throw new NotSupportedException();

        public string? RouteUrl(UrlRouteContext routeContext) => throw new NotSupportedException();
    }

    private sealed class TestUsers : IUserRepository
    {
        public Task<User?> FindByLoginAsync(string login, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<User?>(new User
            {
                Id = 1,
                Name = "Visitor",
                Username = "visitor",
                Email = "visitor@example.com",
                PasswordHash = "valid"
            });
        }

        public Task<long> CreateAsync(
            string name,
            string username,
            string email,
            string cpf,
            string passwordHash,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestPasswords : IPasswordService
    {
        public string Hash(string password) => throw new NotSupportedException();

        public bool Verify(string password, string encodedHash) => password == "correct-password";
    }

    private sealed class TestAuthenticationService : IAuthenticationService
    {
        public bool SignedIn { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            throw new NotSupportedException();
        }

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            throw new NotSupportedException();
        }

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties)
        {
            SignedIn = true;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            throw new NotSupportedException();
        }
    }
}
