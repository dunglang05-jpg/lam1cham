using System.Net.Http.Headers;
using System.Text.Json;

namespace LamMotCham.Core.Account;

/// <summary>Cấu hình kết nối máy chủ tài khoản (api.php trên hosting của Web Admin).</summary>
/// <param name="Endpoint">Địa chỉ api.php, ví dụ https://dunglam.top/api.php.</param>
/// <param name="ApiKey">Khóa truy cập chung của phần mềm (API_KEY trong config.php của hosting). Nạp lúc build, không nằm trong mã nguồn.</param>
public sealed record AccountClientOptions(Uri Endpoint, string ApiKey)
{
    public static Uri DefaultEndpoint { get; } = new("https://dunglam.top/api.php");

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && Endpoint.Scheme == Uri.UriSchemeHttps;

    /// <summary>Không in khóa truy cập.</summary>
    public override string ToString() => $"{Endpoint} (khóa: {(IsConfigured ? "có" : "chưa có")})";
}

/// <summary>Các thao tác tài khoản với máy chủ.</summary>
public interface IAccountClient
{
    Task<AccountResult> LoginAsync(string username, string password, string machineId, CancellationToken cancellationToken = default);

    Task<AccountResult> RegisterAsync(string fullName, string username, string password, string machineId, CancellationToken cancellationToken = default);

    /// <summary>Danh sách ứng dụng đang bật trên Web Admin (action=listApps). Cần mã phiên của lần đăng nhập này.</summary>
    Task<AppListResult> ListAppsAsync(string token, string machineId, CancellationToken cancellationToken = default);

    /// <summary>Gửi kết quả một lượt cài lên mục "Lịch sử cài" của Web Admin (action=installReport).</summary>
    Task<AccountResult> SendInstallReportAsync(string token, string machineId, InstallReportPayload report, CancellationToken cancellationToken = default);
}

/// <summary>Kết quả lấy danh sách ứng dụng: trạng thái và mảng JSON "apps" nguyên gốc (chưa kiểm tra nội dung).</summary>
public sealed record AppListResult(AccountResult Status, string? AppsJson)
{
    public bool IsSuccess => Status.Kind == AccountResultKind.Success && AppsJson is not null;
}

/// <summary>
/// Gọi api.php theo giao thức sẵn có: POST JSON {action, key, ...}, máy chủ luôn trả JSON {ok, code, message, ...}.
/// Đăng nhập: action=login → {role, name, token}. Đăng ký: action=register.
/// Danh sách ứng dụng: action=listApps → {apps: [...]}. Báo cáo cài: action=installReport.
/// </summary>
public sealed class AccountClient : IAccountClient
{
    private const int MaxResponseChars = 2 * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly AccountClientOptions _options;
    private readonly TimeProvider _time;

    /// <param name="http">HttpClient do bên gọi quản lý vòng đời.</param>
    public AccountClient(HttpClient http, AccountClientOptions options, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        _http = http;
        _options = options;
        _time = time ?? TimeProvider.System;
    }

    public async Task<AccountResult> LoginAsync(string username, string password, string machineId, CancellationToken cancellationToken = default)
    {
        var user = (username ?? "").Trim();
        var reply = await SendAsync(
            [
                ("action", "login"),
                ("username", user),
                ("password", password ?? ""),
                ("machine", machineId ?? ""),
            ],
            cancellationToken).ConfigureAwait(false);

        if (reply.Failure is not null)
        {
            return reply.Failure;
        }

        if (!reply.Ok)
        {
            return Fail(reply.Code, reply.Message);
        }

        if (!AccountValidation.IsValidToken(reply.Token))
        {
            return new AccountResult(AccountResultKind.ServerError, "Máy chủ đăng nhập trả về phiên không hợp lệ. Vui lòng thử lại.");
        }

        var now = _time.GetUtcNow();
        var session = new AccountSession(
            user.ToLowerInvariant(),
            string.IsNullOrWhiteSpace(reply.Name) ? user : reply.Name.Trim(),
            ParseRole(reply.Role),
            reply.Token!,
            now);
        return new AccountResult(AccountResultKind.Success, Or(reply.Message, "Đăng nhập thành công."), session);
    }

    public async Task<AccountResult> RegisterAsync(string fullName, string username, string password, string machineId, CancellationToken cancellationToken = default)
    {
        var reply = await SendAsync(
            [
                ("action", "register"),
                ("name", AccountValidation.NormalizeName(fullName)),
                ("username", (username ?? "").Trim()),
                ("password", password ?? ""),
                ("machine", machineId ?? ""),
            ],
            cancellationToken).ConfigureAwait(false);

        if (reply.Failure is not null)
        {
            return reply.Failure;
        }

        return reply.Ok
            ? new AccountResult(AccountResultKind.Registered, Or(reply.Message, "Đăng ký thành công. Tài khoản chưa được kích hoạt."))
            : Fail(reply.Code, reply.Message);
    }

    public async Task<AppListResult> ListAppsAsync(string token, string machineId, CancellationToken cancellationToken = default)
    {
        if (!AccountValidation.IsValidToken(token))
        {
            return new AppListResult(new AccountResult(AccountResultKind.SessionExpired, "Chưa có phiên đăng nhập hợp lệ."), null);
        }

        var reply = await SendAsync(
            w =>
            {
                w.WriteString("action", "listApps");
                w.WriteString("token", token);
                w.WriteString("machine", machineId ?? "");
            },
            cancellationToken).ConfigureAwait(false);

        if (reply.Failure is not null)
        {
            return new AppListResult(reply.Failure, null);
        }

        if (!reply.Ok)
        {
            return new AppListResult(Fail(reply.Code, reply.Message), null);
        }

        return reply.AppsJson is null
            ? new AppListResult(new AccountResult(AccountResultKind.ServerError, "Máy chủ không trả về danh sách ứng dụng."), null)
            : new AppListResult(new AccountResult(AccountResultKind.Success, "Đã lấy danh sách ứng dụng."), reply.AppsJson);
    }

    public async Task<AccountResult> SendInstallReportAsync(string token, string machineId, InstallReportPayload report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!AccountValidation.IsValidToken(token))
        {
            return new AccountResult(AccountResultKind.SessionExpired, "Chưa có phiên đăng nhập hợp lệ.");
        }

        var reply = await SendAsync(
            w =>
            {
                w.WriteString("action", "installReport");
                w.WriteString("token", token);
                w.WriteString("machine", machineId ?? "");
                report.WriteTo(w);
            },
            cancellationToken).ConfigureAwait(false);

        if (reply.Failure is not null)
        {
            return reply.Failure;
        }

        return reply.Ok ? new AccountResult(AccountResultKind.Success, Or(reply.Message, "Đã gửi báo cáo cài.")) : Fail(reply.Code, reply.Message);
    }

    /// <summary>Đổi mã lỗi của máy chủ sang loại kết quả; giữ nguyên thông báo tiếng Việt của máy chủ.</summary>
    internal static AccountResult Fail(string? code, string? message)
    {
        var kind = code switch
        {
            "bad" => AccountResultKind.BadCredentials,
            "pending" => AccountResultKind.Pending,
            "locked" => AccountResultKind.Locked,
            "exists" => AccountResultKind.Exists,
            "invalid" => AccountResultKind.Invalid,
            "limit" => AccountResultKind.RateLimited,
            "session" => AccountResultKind.SessionExpired,
            "forbidden" => AccountResultKind.Forbidden,
            _ => AccountResultKind.ServerError,
        };

        var fallback = kind switch
        {
            AccountResultKind.BadCredentials => "Sai tên đăng nhập hoặc mật khẩu.",
            AccountResultKind.Pending => "Tài khoản chưa được kích hoạt. Vui lòng liên hệ quản trị viên.",
            AccountResultKind.Locked => "Tài khoản đang bị khóa. Vui lòng thử lại sau hoặc liên hệ quản trị viên.",
            AccountResultKind.Exists => "Tên đăng nhập đã tồn tại.",
            AccountResultKind.Invalid => "Thông tin nhập chưa hợp lệ.",
            AccountResultKind.RateLimited => "Bạn thao tác quá nhiều lần. Vui lòng thử lại sau.",
            AccountResultKind.SessionExpired => "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.",
            AccountResultKind.Forbidden => "Tài khoản không có quyền thực hiện thao tác này.",
            _ => "Máy chủ đăng nhập đang gặp lỗi. Vui lòng thử lại sau.",
        };

        return new AccountResult(kind, Or(message, fallback));
    }

    internal static AccountRole ParseRole(string? role) =>
        string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase) ? AccountRole.Admin : AccountRole.Guest;

    private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private Task<Reply> SendAsync(IReadOnlyList<(string Name, string Value)> fields, CancellationToken cancellationToken) =>
        SendAsync(
            w =>
            {
                foreach (var (name, value) in fields)
                {
                    w.WriteString(name, value);
                }
            },
            cancellationToken);

    private async Task<Reply> SendAsync(Action<Utf8JsonWriter> writeFields, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
        {
            return Reply.Failed(AccountResultKind.NotConfigured,
                "Bản phần mềm này chưa được cấu hình máy chủ đăng nhập. Vui lòng tải bản chính thức hoặc liên hệ người phát hành.");
        }

        using var content = new ByteArrayContent(BuildBody(_options.ApiKey, writeFields));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);

        string text;
        int status;
        try
        {
            using var response = await _http.PostAsync(_options.Endpoint, content, timeout.Token).ConfigureAwait(false);
            status = (int)response.StatusCode;
            text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Reply.Failed(AccountResultKind.Network, "Máy chủ đăng nhập không phản hồi. Kiểm tra Internet rồi thử lại.");
        }
        catch (HttpRequestException)
        {
            return Reply.Failed(AccountResultKind.Network, "Không kết nối được máy chủ đăng nhập. Kiểm tra Internet rồi thử lại.");
        }

        if (status < 200 || status > 299)
        {
            return Reply.Failed(AccountResultKind.ServerError, $"Máy chủ đăng nhập trả về lỗi (HTTP {status}). Vui lòng thử lại sau.");
        }

        return Parse(text);
    }

    internal static byte[] BuildBody(string apiKey, IReadOnlyList<(string Name, string Value)> fields) =>
        BuildBody(apiKey, w =>
        {
            foreach (var (name, value) in fields)
            {
                w.WriteString(name, value);
            }
        });

    internal static byte[] BuildBody(string apiKey, Action<Utf8JsonWriter> writeFields)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("key", apiKey);
            writeFields(json);
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    internal static Reply Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxResponseChars)
        {
            return Reply.Failed(AccountResultKind.ServerError, "Máy chủ đăng nhập trả về dữ liệu không đọc được. Vui lòng thử lại sau.");
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Reply.Failed(AccountResultKind.ServerError, "Máy chủ đăng nhập trả về dữ liệu không đọc được. Vui lòng thử lại sau.");
            }

            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            var apps = root.TryGetProperty("apps", out var appsEl) && appsEl.ValueKind == JsonValueKind.Array ? appsEl.GetRawText() : null;
            return new Reply(ok, Str(root, "code"), Str(root, "message"), Str(root, "role"), Str(root, "name"), Str(root, "token"), null) { AppsJson = apps };
        }
        catch (JsonException)
        {
            return Reply.Failed(AccountResultKind.ServerError, "Máy chủ đăng nhập trả về dữ liệu không đọc được. Vui lòng thử lại sau.");
        }
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    internal sealed record Reply(bool Ok, string? Code, string? Message, string? Role, string? Name, string? Token, AccountResult? Failure)
    {
        /// <summary>Mảng "apps" nguyên gốc khi gọi listApps.</summary>
        public string? AppsJson { get; init; }

        public static Reply Failed(AccountResultKind kind, string message) =>
            new(false, null, null, null, null, null, new AccountResult(kind, message));

        /// <summary>Không in mã phiên.</summary>
        public override string ToString() => $"ok={Ok}, code={Code}";
    }
}
