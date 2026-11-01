using System.Net.Http.Headers;
using System.Reflection;
using LamMotCham.Core.Account;
using LamMotCham.Core.Logging;
using LamMotCham.Windows.Account;

namespace LamMotCham.Presentation.Services;

/// <summary>
/// Mô-đun "account": đăng nhập, đăng ký với máy chủ Web Admin (api.php), lấy danh sách ứng dụng và gửi báo cáo cài (QĐ-006).
/// Phiên chỉ nằm trong bộ nhớ của lần chạy này: không lưu xuống đĩa, thoát phần mềm là mất, lần sau phải đăng nhập lại.
/// Log chỉ ghi tên đăng nhập và kết quả; không ghi mật khẩu hay mã phiên.
/// </summary>
public sealed class AccountService : IDisposable
{
    /// <summary>Khóa AssemblyMetadata do MSBuild nạp lúc build (xem LamMotCham.App.csproj).</summary>
    internal const string ApiKeyMetadata = "LmcApiKey";
    internal const string EndpointMetadata = "LmcAccountEndpoint";

    private readonly IAccountClient _client;
    private readonly IRunLogger _log;
    private readonly HttpClient? _ownedHttp;

    public AccountService(IAccountClient client, AccountClientOptions options, string machineId, IRunLogger log, HttpClient? ownedHttp = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        _client = client;
        _log = log;
        _ownedHttp = ownedHttp;
        Endpoint = options.Endpoint;
        IsConfigured = options.IsConfigured;
        MachineId = machineId;
    }

    public Uri Endpoint { get; }

    public bool IsConfigured { get; }

    public string MachineId { get; }

    /// <summary>Phiên hiện tại; null khi chưa đăng nhập hoặc đã đăng xuất.</summary>
    public AccountSession? Current { get; private set; }

    public static AccountService CreateDefault(IRunLogger log, string appVersion)
    {
        var options = ReadOptions(Assembly.GetEntryAssembly());
        var http = new HttpClient(new SocketsHttpHandler
        {
            // api.php luôn trả JSON trực tiếp; chuyển hướng (ví dụ POST bị đổi thành GET) bị coi là lỗi thay vì đọc nhầm kết quả.
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
        });
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LamMotCham", SafeVersion(appVersion)));
        var service = new AccountService(new AccountClient(http, options), options, MachineIdentity.Get(), log, http);
        if (!service.IsConfigured)
        {
            log.Warning("Bản build chưa có khóa máy chủ đăng nhập (LmcApiKey): không đăng nhập được.");
        }

        return service;
    }

    /// <summary>Đọc khóa và địa chỉ máy chủ từ AssemblyMetadata của file EXE.</summary>
    internal static AccountClientOptions ReadOptions(Assembly? assembly)
    {
        var meta = assembly?.GetCustomAttributes<AssemblyMetadataAttribute>().ToList() ?? [];
        var key = meta.FirstOrDefault(m => m.Key == ApiKeyMetadata)?.Value ?? "";
        var endpointText = meta.FirstOrDefault(m => m.Key == EndpointMetadata)?.Value;
        var endpoint = Uri.TryCreate(endpointText, UriKind.Absolute, out var parsed) ? parsed : AccountClientOptions.DefaultEndpoint;
        return new AccountClientOptions(endpoint, key.Trim());
    }

    public async Task<AccountResult> SignInAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var result = await _client.LoginAsync(username, password, MachineId, cancellationToken).ConfigureAwait(false);
        var user = (username ?? "").Trim();
        if (result.Kind == AccountResultKind.Success && result.Session is not null)
        {
            Current = result.Session;
            _log.Info($"Đăng nhập thành công: {result.Session}.");
        }
        else
        {
            _log.Warning($"Đăng nhập không thành công: {user} ({result.Kind}).");
        }

        return result;
    }

    /// <summary>
    /// Thử vào bằng Wi-Fi đang kết nối (danh sách "Wi-Fi miễn đăng nhập" trên Admin). Không được thì trả null
    /// và màn hình đăng nhập hiện như thường. Chỉ ghi tên Wi-Fi vào log, không ghi mã phiên.
    /// </summary>
    public async Task<AccountResult?> TrySignInByWifiAsync(IReadOnlyList<string> ssids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ssids);
        if (!IsConfigured)
        {
            return null;
        }

        foreach (var ssid in ssids.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal))
        {
            var result = await _client.WifiLoginAsync(ssid, MachineId, cancellationToken).ConfigureAwait(false);
            if (result.Kind == AccountResultKind.Success && result.Session is not null)
            {
                Current = result.Session;
                _log.Info($"Vào không cần đăng nhập qua Wi-Fi \"{ssid}\" (tài khoản {result.Session.DisplayName}).");
                return result;
            }

            _log.Info($"Wi-Fi \"{ssid}\" không nằm trong danh sách miễn đăng nhập ({result.Kind}).");
        }

        return null;
    }

    public async Task<AccountResult> RegisterAsync(string fullName, string username, string password, CancellationToken cancellationToken = default)
    {
        var result = await _client.RegisterAsync(fullName, username, password, MachineId, cancellationToken).ConfigureAwait(false);
        var user = (username ?? "").Trim();
        _log.Info(result.IsSuccess
            ? $"Đăng ký tài khoản {user}: thành công, chờ quản trị viên duyệt."
            : $"Đăng ký tài khoản {user}: không thành công ({result.Kind}).");
        return result;
    }

    /// <summary>Lấy danh sách ứng dụng trên Web Admin bằng phiên hiện tại (QĐ-006). Không ghi mã phiên vào log.</summary>
    public async Task<AppListResult> LoadAppsAsync(CancellationToken cancellationToken = default)
    {
        if (Current is not { } session)
        {
            return new AppListResult(new AccountResult(AccountResultKind.SessionExpired, "Chưa đăng nhập."), null);
        }

        var result = await _client.ListAppsAsync(session.Token, MachineId, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            _log.Info("Đã lấy danh sách ứng dụng từ Web Admin.");
        }
        else
        {
            _log.Warning($"Không lấy được danh sách ứng dụng ({result.Status.Kind}): {result.Status.Message}");
        }

        return result;
    }

    /// <summary>Gửi kết quả lượt cài lên mục "Lịch sử cài" (QĐ-006). Lỗi gửi chỉ ghi log.</summary>
    public async Task<AccountResult> SendInstallReportAsync(InstallReportPayload report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (Current is not { } session)
        {
            return new AccountResult(AccountResultKind.SessionExpired, "Chưa đăng nhập.");
        }

        var result = await _client.SendInstallReportAsync(session.Token, MachineId, report, cancellationToken).ConfigureAwait(false);
        if (result.Kind == AccountResultKind.Success)
        {
            _log.Info($"Đã gửi báo cáo cài ({report.Items.Count} ứng dụng) lên Lịch sử cài.");
        }
        else
        {
            _log.Warning($"Không gửi được báo cáo cài ({result.Kind}): {result.Message}");
        }

        return result;
    }

    /// <summary>Xóa phiên khỏi bộ nhớ.</summary>
    public void SignOut()
    {
        if (Current is not null)
        {
            _log.Info($"Đăng xuất: {Current.Username}.");
        }

        Current = null;
    }

    public void Dispose()
    {
        Current = null;
        _ownedHttp?.Dispose();
    }

    private static string SafeVersion(string version)
    {
        var v = new string((version ?? "").TakeWhile(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-').ToArray());
        return v.Length == 0 ? "0" : v;
    }
}
