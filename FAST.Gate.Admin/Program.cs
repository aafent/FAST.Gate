using FAST.Gate.Admin.Services;
using FAST.Gate.Client.Extensions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());

// Pure Blazor Server — no static SSR, no rendermode directives needed
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath  = "/login";
        options.LogoutPath = "/login";
    });

builder.Services.AddAuthorization();
builder.Services.AddFastGateClient(builder.Configuration);

// Operation log — singleton so it persists across navigations
builder.Services.AddSingleton<OperationLogService>();

// Gate settings service
builder.Services.AddScoped<GateSettingsService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
