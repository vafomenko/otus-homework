namespace Blt.Users.Api.Data;

public sealed class BillingAccounts(HttpClient client, IConfiguration configuration)
{
    public async Task CreateAsync(long userId, CancellationToken ct)
    {
        var baseUrl = configuration["Billing:BaseUrl"];
        // Earlier homework deployments remain standalone. The homework-8 chart always sets this URL.
        if (string.IsNullOrWhiteSpace(baseUrl)) return;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "/internal/accounts"));
        request.Headers.Add("X-Service-Key", configuration["Internal:ApiKey"]);
        request.Content = JsonContent.Create(new { userId });
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
