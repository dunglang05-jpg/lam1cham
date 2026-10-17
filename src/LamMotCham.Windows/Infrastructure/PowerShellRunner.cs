using System.Text;
using System.Text.Json;
using LamMotCham.Core.Processes;

namespace LamMotCham.Windows.Infrastructure;

/// <summary>
/// Chạy script PowerShell <b>đóng gói sẵn trong ứng dụng</b> và đọc kết quả JSON.
/// Không bao giờ nhận script hoặc tham số từ người dùng hay từ mạng.
/// </summary>
internal static class PowerShellRunner
{
    private const string Prelude =
        "$ProgressPreference = 'SilentlyContinue'\n" +
        "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n";

    public static async Task<JsonDocument> RunJsonAsync(string embeddedScript, CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Prelude + embeddedScript));
        var result = await ProcessRunner.RunAsync(
            WindowsEnvironment.PowerShellPath,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
            cancellationToken,
            maxOutputChars: 2_000_000).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"PowerShell trả về mã {result.ExitCode}. {Truncate(result.StandardError, 400)}");
        }

        var json = result.StandardOutput.Trim();
        var start = json.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException("PowerShell không trả về dữ liệu JSON.");
        }

        return JsonDocument.Parse(json[start..]);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
