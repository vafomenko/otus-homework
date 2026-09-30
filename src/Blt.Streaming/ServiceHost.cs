using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blt.Streaming;

public static class ServiceHost
{
    public static WebApplicationBuilder Create(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args.Where(a => a != "--migrate").ToArray());
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole();
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton<Database>();
        builder.Services.AddHttpClient("users", client =>
            client.BaseAddress = new Uri(builder.Configuration["Users:BaseUrl"] ?? "http://users:8000"));
        builder.Services.AddAuthentication("session")
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, UserAuthentication>("session", _ => { });
        builder.Services.AddAuthorization();
        return builder;
    }

    public static async Task<bool> PrepareAsync(WebApplication app, string[] args, Assembly assembly)
    {
        if (args.Contains("--migrate"))
        {
            await app.Services.GetRequiredService<Database>().MigrateAsync(assembly);
            await app.DisposeAsync();
            return false;
        }
        app.UseExceptionHandler();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            await next(context);
        });
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health", () => Results.Ok(new { status = "OK" }));
        app.MapGet("/ready", async (Database db, CancellationToken ct) =>
        {
            await db.PingAsync(ct);
            return Results.Ok(new { status = "READY" });
        });
        return true;
    }
}
