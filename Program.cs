using asterlinkportaldepagamento.Data;
using asterlinkportaldepagamento.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

builder.Services.AddHttpClient(
    "MercadoPago",
    client =>
{
    client.BaseAddress = new Uri("https://api.mercadopago.com/");
    client.Timeout = TimeSpan.FromSeconds(20);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
.AddCookie(options =>
{
    options.LoginPath = "/conta";
    options.AccessDeniedPath = "/conta";
    options.Cookie.Name = "AsterLink.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorization();

builder.Services.AddScoped<IUserRepository, UserRepository>();

builder.Services.AddScoped<IPlanRepository, PlanRepository>();

builder.Services.AddScoped<IAccessSessionRepository, AccessSessionRepository>();

builder.Services.AddSingleton<IPasswordService, PasswordService>();

var dataProtection = builder.Services.AddDataProtection();

var keyDirectory = builder.Configuration["Network:DataProtectionKeyPath"];

if (!string.IsNullOrWhiteSpace(keyDirectory))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));

    if (OperatingSystem.IsWindows())
    {
        dataProtection.ProtectKeysWithDpapi();
    }
}

builder.Services.Configure<NetworkOptions>(builder.Configuration.GetSection("Network"));

builder.Services.AddScoped<NetworkOrderRepository>();

builder.Services.AddScoped<MercadoPagoPayments>();

builder.Services.AddScoped<RouterOsHotspot>();

builder.Services.AddScoped<PaymentReconciliation>();

builder.Services.AddHostedService<NetworkProvisioningWorker>();

builder.Services.AddHttpClient("RouterOS", client => client.Timeout = TimeSpan.FromSeconds(15))
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");

    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();

app.MapStaticAssets();

app.MapGet("/", () => Results.Redirect("/conta"));

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Index}/{id?}")
.WithStaticAssets();

app.Run();
