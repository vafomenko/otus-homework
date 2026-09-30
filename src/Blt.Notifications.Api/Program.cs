using System.Security.Claims;
using Blt.Streaming;

namespace Blt.Notifications.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = ServiceHost.Create(args);
        builder.Services.AddSingleton<NotificationRepository>();
        builder.Services.AddHostedService<OrderConsumer>();
        var app = builder.Build();
        if (!await ServiceHost.PrepareAsync(app, args, typeof(Program).Assembly)) return;
        app.MapGet("/notifications", async (Guid? orderId, ClaimsPrincipal user, NotificationRepository db, CancellationToken ct) =>
            Results.Ok(await db.ListAsync(CurrentUser.Id(user), orderId, ct))).RequireAuthorization();
        app.MapPost("/notifications", async (SendNotificationRequest request, ClaimsPrincipal user, NotificationRepository db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > 200 ||
                string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 10000) return Results.BadRequest();
            var message = new Notification(Guid.NewGuid(), Guid.NewGuid(), null, CurrentUser.Id(user), CurrentUser.Email(user),
                request.Subject, request.Body, null, DateTimeOffset.UtcNow);
            await db.SaveAsync(message, ct);
            return Results.Created("/notifications", message);
        }).RequireAuthorization();
        await app.RunAsync();
    }
}
