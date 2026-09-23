using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Blt.Users.Api.Models;
using Blt.Users.Api.Security;
using Npgsql;
using Xunit;

namespace Blt.Users.Api.Tests;

public sealed class ProfileAccessTests(PostgresFixture database) : IClassFixture<PostgresFixture>
{
    private static RegisterRequest NewRegistration() => new(
        "user_" + Guid.NewGuid().ToString("N"), "Passphrase!" + Guid.NewGuid().ToString("N"),
        "First", "Last", Guid.NewGuid().ToString("N") + "@example.com", "+79990000001");

    private static async Task<UserResponse> RegisterAsync(HttpClient client, RegisterRequest request)
    {
        using var response = await client.PostAsJsonAsync("/auth/register", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(request.Password!, text);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        return (await response.Content.ReadFromJsonAsync<UserResponse>())!;
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client, RegisterRequest registration)
    {
        using var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(registration.Username, registration.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal("Bearer", login.TokenType);
        Assert.Equal(43, login.AccessToken.Length);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return login;
    }

    [Fact]
    public async Task TwoUsersCanOnlyReadEditListAndDeleteTheirOwnProfileAcrossEveryAlias()
    {
        using var first = database.App.CreateClient();
        using var second = database.App.CreateClient();
        var registration1 = NewRegistration();
        var registration2 = NewRegistration();
        var user1 = await RegisterAsync(first, registration1);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync($"/user/{user1.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.PutAsJsonAsync($"/user/{user1.Id}", new { firstName = "Stolen" })).StatusCode);
        var login1 = await LoginAsync(first, registration1);
        Assert.Equal(user1.Id, login1.UserId);
        var changedEmail = Guid.NewGuid().ToString("N") + "@example.com";
        using var update = await first.PutAsJsonAsync($"/user/{user1.Id}", new { firstName = "Changed", email = changedEmail });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var changed = await first.GetFromJsonAsync<UserResponse>("/profile");
        Assert.Equal("Changed", changed!.FirstName);
        Assert.Equal(changedEmail, changed.Email);
        Assert.Equal(user1.Username, changed.Username);

        Assert.Equal(HttpStatusCode.NoContent, (await first.PostAsync("/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetAsync("/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.PutAsJsonAsync("/profile", new { firstName = "After logout" })).StatusCode);
        var user2 = await RegisterAsync(second, registration2);
        await LoginAsync(second, registration2);

        foreach (var prefix in SecurityBoundaryTests.Prefixes)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await second.GetAsync($"{prefix}/{user1.Id}")).StatusCode);
            // Authorization precedes body validation and does not disclose resource existence.
            Assert.Equal(HttpStatusCode.Forbidden, (await second.PutAsJsonAsync($"{prefix}/{user1.Id}", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await second.DeleteAsync($"{prefix}/{user1.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await second.GetAsync($"{prefix}/{long.MaxValue}")).StatusCode);
            var list = await second.GetFromJsonAsync<UserResponse[]>(prefix);
            Assert.Equal(user2.Id, Assert.Single(list!).Id);
            Assert.Equal(user2.Id, (await second.GetFromJsonAsync<UserResponse>($"{prefix}/{user2.Id}"))!.Id);
            Assert.Equal(HttpStatusCode.OK, (await second.PutAsJsonAsync($"{prefix}/{user2.Id}", new { firstName = "Own" })).StatusCode);
            // The old passwordless creation endpoint is closed.
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await second.PostAsJsonAsync(prefix, registration2.Profile)).StatusCode);
        }

        second.DefaultRequestHeaders.Add("X-User-Id", user1.Id.ToString());
        second.DefaultRequestHeaders.Add("X-Forwarded-User", user1.Id.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await second.GetAsync($"/user/{user1.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.PutAsJsonAsync("/profile", new { id = user1.Id, firstName = "Own again", password = "Overwritten!" })).StatusCode);
        Assert.Equal(user2.Id, (await second.GetFromJsonAsync<UserResponse>($"/profile?userId={user1.Id}"))!.Id);
        await LoginAsync(first, registration1);
        Assert.Equal(changed, await first.GetFromJsonAsync<UserResponse>($"/user/{user1.Id}"));

        Assert.Equal(HttpStatusCode.NoContent, (await second.DeleteAsync($"/user/{user2.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/profile")).StatusCode);
    }

    [Fact]
    public async Task SessionIsSharedAndLogoutOnAnotherReplicaRevokesOnlyThatSession()
    {
        using var client = database.App.CreateClient();
        var registration = NewRegistration();
        var user = await RegisterAsync(client, registration);
        var original = await LoginAsync(client, registration);
        await using var replica = database.CreateApp();
        using var other = replica.CreateClient();
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", original.AccessToken);
        Assert.Equal(user.Id, (await other.GetFromJsonAsync<UserResponse>("/profile"))!.Id);
        var newer = await LoginAsync(client, registration);
        Assert.NotEqual(original.AccessToken, newer.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await other.PostAsync("/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/profile")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", original.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/profile")).StatusCode);
    }

    [Fact]
    public async Task ExpiredAndTamperedTokensCannotReadOrEditProfiles()
    {
        var time = new AdjustableTime();
        await using var app = database.CreateApp(time);
        using var client = app.CreateClient();
        var registration = NewRegistration();
        await RegisterAsync(client, registration);
        var login = await LoginAsync(client, registration);
        var replacement = login.AccessToken[0] == 'a' ? 'b' : 'a';
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", replacement + login.AccessToken[1..]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/profile")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        time.Now = login.ExpiresAt;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync("/profile", new { firstName = "Expired" })).StatusCode);
    }

    [Fact]
    public async Task PasswordsAreSaltedAndOnlyTokenHashesAreStored()
    {
        using var client = database.App.CreateClient();
        var request1 = NewRegistration();
        var request2 = NewRegistration() with { Password = request1.Password };
        var user1 = await RegisterAsync(client, request1);
        var user2 = await RegisterAsync(client, request2);
        var login = await LoginAsync(client, request1);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT password_hash FROM user_credentials WHERE user_id = ANY(@ids) ORDER BY user_id", connection);
        command.Parameters.AddWithValue("ids", new[] { user1.Id, user2.Id });
        var hashes = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) hashes.Add(reader.GetString(0));
        Assert.Equal(2, hashes.Count);
        Assert.NotEqual(hashes[0], hashes[1]);
        foreach (var hash in hashes)
        {
            Assert.NotEqual(request1.Password, hash);
            Assert.True(new PasswordService().Verify(hash, request1.Password!));
        }
        command.Parameters.Clear();
        command.CommandText = "SELECT token_hash FROM user_sessions WHERE user_id = @id";
        command.Parameters.AddWithValue("id", user1.Id);
        var tokenHash = (string)(await command.ExecuteScalarAsync())!;
        Assert.NotEqual(login.AccessToken, tokenHash);
        Assert.Equal(SessionToken.Hash(login.AccessToken), tokenHash);
    }

    [Fact]
    public async Task WrongUnknownAndLegacyPasswordsReturnTheSameErrorAndUpgradePreservesData()
    {
        using var client = database.App.CreateClient();
        var registration = NewRegistration();
        await RegisterAsync(client, registration);
        var bodies = new List<string>();
        foreach (var username in new[] { registration.Username!, Guid.NewGuid().ToString("N"), "legacy" })
        {
            using var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(username, "IncorrectPassword!"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            bodies.Add(await response.Content.ReadAsStringAsync());
        }
        Assert.Single(bodies.Distinct());
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM users WHERE username = 'legacy'", connection);
        Assert.Equal(1L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations";
        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ConcurrentDuplicateRegistrationCreatesExactlyOneCompleteAccount()
    {
        await using var app = database.CreateApp();
        using var client = app.CreateClient();
        var registration = NewRegistration();
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/auth/register", registration),
            client.PostAsJsonAsync("/auth/register", registration));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses) response.Dispose();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/auth/register", NewRegistration() with { Email = registration.Email })).StatusCode);
        await LoginAsync(client, registration);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM users u JOIN user_credentials c ON c.user_id = u.id WHERE u.username = @username", connection);
        command.Parameters.AddWithValue("username", registration.Username!);
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ConflictingProfileUpdateIsAtomicAndLoginUsesTheUpdatedUsername()
    {
        using var client = database.App.CreateClient();
        var registration = NewRegistration();
        var otherRegistration = NewRegistration();
        await RegisterAsync(client, registration);
        await RegisterAsync(client, otherRegistration);
        await LoginAsync(client, registration);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/profile", new { email = otherRegistration.Email, firstName = "Conflict" })).StatusCode);
        Assert.Equal(registration.FirstName, (await client.GetFromJsonAsync<UserResponse>("/profile"))!.FirstName);
        var username = "renamed_" + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/profile", new { username })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/auth/login", new LoginRequest(registration.Username, registration.Password))).StatusCode);
        await LoginAsync(client, registration with { Username = username });
    }

    [Theory]
    [InlineData("password", "short")]
    [InlineData("email", null)]
    [InlineData("email", "not-an-email")]
    [InlineData("username", " ")]
    [InlineData("firstName", " ")]
    [InlineData("phone", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task InvalidRegistrationDoesNotCreateAnAccount(string field, string? value)
    {
        await using var app = database.CreateApp();
        using var client = app.CreateClient();
        var request = NewRegistration();
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(request, JsonSerializerOptions.Web))!;
        body[field] = value;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/auth/register", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/auth/login", new LoginRequest(request.Username, request.Password))).StatusCode);
    }

    private sealed class AdjustableTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
