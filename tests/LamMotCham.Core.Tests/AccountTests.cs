using System.Net;
using System.Text;
using System.Text.Json;
using LamMotCham.Core.Account;
using Xunit;

namespace LamMotCham.Core.Tests;

public sealed class AccountClientTests
{
    private const string Token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Login_success_returns_session_with_role_and_name()
    {
        var (client, handler) = Create("""{"ok":true,"code":"ok","message":"Đăng nhập thành công.","role":"admin","name":"Lăng Dũng","token":"TOKEN"}""".Replace("TOKEN", Token, StringComparison.Ordinal));

        var result = await client.LoginAsync("  DungLang ", "matkhau1", "lmc-abc");

        Assert.Equal(AccountResultKind.Success, result.Kind);
        Assert.NotNull(result.Session);
        Assert.Equal("dunglang", result.Session!.Username);
        Assert.Equal("Lăng Dũng", result.Session.DisplayName);
        Assert.Equal(AccountRole.Admin, result.Session.Role);
        Assert.Equal(Token, result.Session.Token);
        Assert.Equal("D", result.Session.Initial);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Login_sends_protocol_fields_with_api_key()
    {
        var (client, handler) = Create("""{"ok":false,"code":"bad","message":"Sai tên đăng nhập hoặc mật khẩu."}""");

        await client.LoginAsync("dunglang", "matkhau1", "lmc-abc");

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(new Uri("https://example.test/api.php"), handler.LastUri);
        Assert.Equal("application/json", handler.LastContentType);
        using var body = JsonDocument.Parse(handler.LastBody!);
        var root = body.RootElement;
        Assert.Equal("khoa-thu", root.GetProperty("key").GetString());
        Assert.Equal("login", root.GetProperty("action").GetString());
        Assert.Equal("dunglang", root.GetProperty("username").GetString());
        Assert.Equal("matkhau1", root.GetProperty("password").GetString());
        Assert.Equal("lmc-abc", root.GetProperty("machine").GetString());
    }

    [Theory]
    [InlineData("bad", AccountResultKind.BadCredentials)]
    [InlineData("pending", AccountResultKind.Pending)]
    [InlineData("locked", AccountResultKind.Locked)]
    [InlineData("limit", AccountResultKind.RateLimited)]
    [InlineData("error", AccountResultKind.ServerError)]
    [InlineData("la", AccountResultKind.ServerError)]
    public async Task Login_failure_codes_map_and_keep_server_message(string code, AccountResultKind expected)
    {
        var (client, _) = Create($$"""{"ok":false,"code":"{{code}}","message":"Thông báo của máy chủ."}""");

        var result = await client.LoginAsync("dunglang", "matkhau1", "lmc-abc");

        Assert.Equal(expected, result.Kind);
        Assert.Equal("Thông báo của máy chủ.", result.Message);
        Assert.Null(result.Session);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Pending_account_without_message_gets_vietnamese_fallback()
    {
        var (client, _) = Create("""{"ok":false,"code":"pending"}""");

        var result = await client.LoginAsync("dunglang", "matkhau1", "lmc-abc");

        Assert.Equal(AccountResultKind.Pending, result.Kind);
        Assert.Contains("chưa được kích hoạt", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_ok_with_malformed_token_is_rejected()
    {
        var (client, _) = Create("""{"ok":true,"code":"ok","role":"khach","name":"A","token":"abc"}""");

        var result = await client.LoginAsync("dunglang", "matkhau1", "lmc-abc");

        Assert.Equal(AccountResultKind.ServerError, result.Kind);
        Assert.Null(result.Session);
    }

    [Fact]
    public async Task Guest_role_and_missing_name_fall_back_to_username()
    {
        var (client, _) = Create("""{"ok":true,"code":"ok","role":"khach","token":"TOKEN"}""".Replace("TOKEN", Token, StringComparison.Ordinal));

        var result = await client.LoginAsync("kho01", "matkhau1", "lmc-abc");

        Assert.Equal(AccountRole.Guest, result.Session!.Role);
        Assert.Equal("kho01", result.Session.DisplayName);
        Assert.Equal("Nhân viên", result.Session.RoleText);
    }

    [Fact]
    public async Task Register_success_is_registered_not_signed_in()
    {
        var (client, handler) = Create("""{"ok":true,"code":"registered","message":"Đăng ký thành công. Tài khoản chưa được kích hoạt."}""");

        var result = await client.RegisterAsync("  Nguyễn   Thảo ", "thao.nguyen", "matkhau1", "lmc-abc");

        Assert.Equal(AccountResultKind.Registered, result.Kind);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Session);
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("register", body.RootElement.GetProperty("action").GetString());
        Assert.Equal("Nguyễn Thảo", body.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Register_existing_username_maps_to_exists()
    {
        var (client, _) = Create("""{"ok":false,"code":"exists","message":"Tên đăng nhập / mã kho đã tồn tại."}""");

        var result = await client.RegisterAsync("Nguyễn Thảo", "thao.nguyen", "matkhau1", "lmc-abc");

        Assert.Equal(AccountResultKind.Exists, result.Kind);
    }

    [Fact]
    public async Task Network_failure_is_reported_as_network_problem()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("không có mạng"));
        using var http = new HttpClient(handler);
        var client = new AccountClient(http, Options());

        var result = await client.LoginAsync("dunglang", "matkhau1", "lmc-abc");

        Assert.Equal(AccountResultKind.Network, result.Kind);
        Assert.True(result.IsConnectivityProblem);
    }

    [Fact]
    public async Task Timeout_is_reported_as_network_problem()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Json("{}");
        });
        using var http = new HttpClient(handler);
        var client = new AccountClient(http, Options() with { Timeout = TimeSpan.FromMilliseconds(50) });

        var result = await client.LoginAsync("dunglang", "matkhau1", "lmc-abc");

        Assert.Equal(AccountResultKind.Network, result.Kind);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_swallowed()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Json("{}");
        });
        using var http = new HttpClient(handler);
        var client = new AccountClient(http, Options());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.LoginAsync("dunglang", "matkhau1", "lmc-abc", cts.Token));
    }

    [Theory]
    [InlineData("<html>Lỗi 500</html>")]
    [InlineData("")]
    [InlineData("[1,2]")]
    public async Task Unreadable_reply_is_server_error(string body)
    {
        var (client, _) = Create(body);

        var result = await client.LoginAsync("dunglang", "matkhau1", "lmc-abc");

        Assert.Equal(AccountResultKind.ServerError, result.Kind);
    }

    [Fact]
    public async Task Http_error_status_is_server_error()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("bad gateway") });
        using var http = new HttpClient(handler);
        var client = new AccountClient(http, Options());

        var result = await client.LoginAsync("dunglang", "matkhau1", "lmc-abc");

        Assert.Equal(AccountResultKind.ServerError, result.Kind);
        Assert.Contains("502", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_api_key_never_contacts_server()
    {
        var handler = new FakeHandler(_ => Json("{}"));
        using var http = new HttpClient(handler);
        var client = new AccountClient(http, new AccountClientOptions(new Uri("https://example.test/api.php"), ""));

        var result = await client.LoginAsync("dunglang", "matkhau1", "lmc-abc");

        Assert.Equal(AccountResultKind.NotConfigured, result.Kind);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void Plain_http_endpoint_is_not_configured()
    {
        var options = new AccountClientOptions(new Uri("http://example.test/api.php"), "khoa-thu");

        Assert.False(options.IsConfigured);
        Assert.DoesNotContain("khoa-thu", options.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Session_text_never_contains_token()
    {
        var session = new AccountSession("dunglang", "Lăng Dũng", AccountRole.Admin, Token, DateTimeOffset.UnixEpoch);

        Assert.DoesNotContain(Token, session.ToString(), StringComparison.Ordinal);
    }

    private static AccountClientOptions Options() => new(new Uri("https://example.test/api.php"), "khoa-thu");

    private static (AccountClient Client, FakeHandler Handler) Create(string reply)
    {
        var handler = new FakeHandler(_ => Json(reply));
        var http = new HttpClient(handler);
        return (new AccountClient(http, Options()), handler);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _reply;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> reply)
            : this((request, _) => Task.FromResult(reply(request)))
        {
        }

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) => _reply = reply;

        public int Calls { get; private set; }

        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastUri { get; private set; }

        public string? LastContentType { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastMethod = request.Method;
            LastUri = request.RequestUri;
            LastContentType = request.Content?.Headers.ContentType?.MediaType;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await _reply(request, cancellationToken);
        }
    }
}

public sealed class AccountValidationTests
{
    [Theory]
    [InlineData("kho01", true)]
    [InlineData("thao.nguyen", true)]
    [InlineData("a_b-c", true)]
    [InlineData("ab", false)]
    [InlineData(".abc", false)]
    [InlineData("có dấu", false)]
    [InlineData("ab c", false)]
    [InlineData("  kho01  ", true)]
    [InlineData("", false)]
    public void Username_rules_match_server(string value, bool valid) =>
        Assert.Equal(valid, AccountValidation.CheckUsername(value) is null);

    [Theory]
    [InlineData("Nguyễn Thảo", true)]
    [InlineData("  Nguyễn   Thảo  ", true)]
    [InlineData("A", false)]
    [InlineData("Thảo 2", false)]
    [InlineData("", false)]
    public void Name_rules_match_server(string value, bool valid) =>
        Assert.Equal(valid, AccountValidation.CheckName(value) is null);

    [Theory]
    [InlineData("matkhau1", true)]
    [InlineData("12345", false)]
    [InlineData("mat khau", false)]
    public void New_password_rules_match_server(string value, bool valid) =>
        Assert.Equal(valid, AccountValidation.CheckNewPassword(value) is null);

    [Fact]
    public void Login_password_only_checks_length_so_old_accounts_still_work()
    {
        Assert.Null(AccountValidation.CheckLoginPassword("123"));
        Assert.NotNull(AccountValidation.CheckLoginPassword(""));
        Assert.NotNull(AccountValidation.CheckLoginPassword(new string('x', 65)));
    }

    [Theory]
    [InlineData("lmc-0123abcd", true)]
    [InlineData("khong ro", false)]
    [InlineData("", false)]
    public void Machine_id_rules_match_server(string value, bool valid) =>
        Assert.Equal(valid, AccountValidation.IsValidMachineId(value));
}
