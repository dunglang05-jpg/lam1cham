using LamMotCham.Core.Logging;
using LamMotCham.Core.Tasks;
using LamMotCham.Windows.Software;
using Xunit;

namespace LamMotCham.Windows.Tests;

/// <summary>QĐ-006: đọc danh sách ứng dụng của Web Admin và các kiểm tra an toàn khi cài bằng link.</summary>
public sealed class SyncCatalogTests
{
    private const string AdminList = """
    [
      {"Name":"Google Chrome","Initial":"G","Color":"#4285F4","Kind":"Exe","Checked":true,"AllowUnsigned":false,
       "DirectUrl":"https://dl.google.com/chrome/install/ChromeStandaloneSetup64.exe","PageUrl":"","PageRegex":"","SilentArgs":"/silent /install",
       "WingetId":"Google.Chrome","Website":"https://www.google.com/intl/vi_vn/chrome/","ZipFolder":"","ZipExe":"","Variants":[],
       "DetectNames":["Google Chrome"],"DetectPaths":["%ProgramFiles%\\Google\\Chrome\\Application\\chrome.exe","C:\\Windows\\..\\evil.exe"],
       "Publisher":"Google LLC","MachineWide":true,"Category":"Trình duyệt","Description":"Trình duyệt web của Google"},
      {"Name":"WinRAR","Initial":"R","Color":"#8E44AD","Kind":"Exe","Checked":true,"DirectUrl":"https://www.rarlab.com/rar/winrar-x64-723vn.exe",
       "PageUrl":"https://www.rarlab.com/download.htm","PageRegex":"/rar/winrar-x64-\\d+vn\\.exe","SilentArgs":"/S","WingetId":"RARLab.WinRAR",
       "Publisher":"win.rar GmbH","MachineWide":true,"Category":"Tiện ích","Description":"Nén và giải nén"},
      {"Name":"Microsoft Office","Initial":"O","Color":"#D83B01","Kind":"Exe","Checked":false,"Variants":[
         {"Label":"Home 2024 - Tiếng Việt (64-bit)","Kind":"Exe","DirectUrl":"https://c2rsetup.officeapps.live.com/c2r/download.aspx?ProductreleaseID=Home2024Retail&platform=x64&language=vi-vn&version=O16GA","SilentArgs":""},
         {"Label":"Home 2024 (link cửa hàng - OneDrive)","Kind":"Web","DirectUrl":"https://onedrive.live.com/?id=abc","SilentArgs":""},
         {"Label":"Hỏng","Kind":"Exe","DirectUrl":"http://example.com/x.exe","SilentArgs":""}],
       "DetectNames":["Microsoft Office","Microsoft 365"],"Category":"Văn phòng"},
      {"Name":"WhatsApp","Initial":"Wa","Color":"#25D366","Kind":"Exe","WingetId":"","Website":"https://www.whatsapp.com/download?lang=vi",
       "Category":"Liên lạc","Description":"Chỉ cài được từ Microsoft Store"},
      {"Name":"WPS Office","Initial":"W","Color":"#E5383B","Kind":"Exe","WingetId":"Kingsoft.WPSOffice","Publisher":"","Category":"Văn phòng"},
      {"Name":"YouTube","Initial":"Yt","Color":"#FF0000","Kind":"Web","Website":"https://www.youtube.com/?hl=vi","Category":"Trang web"},
      {"Name":"Độc hại","Initial":"X","Color":"red","Kind":"Exe","DirectUrl":"https://example.com/x.exe","SilentArgs":"/S & calc","WingetId":"Evil.App & calc"},
      {"Name":"Google Chrome","Kind":"Exe","WingetId":"Google.Chrome","Publisher":"Google LLC"},
      {"Name":"<script>","Kind":"Exe"},
      "chuỗi lạ"
    ]
    """;

    [Fact]
    public void Admin_list_is_parsed_with_order_defaults_and_vietnamese_fields()
    {
        var result = RemoteCatalog.Parse(AdminList);
        var names = result.Packages.Select(p => p.Name).ToList();

        Assert.Equal(new[] { "Google Chrome", "WinRAR", "Microsoft Office", "WhatsApp", "WPS Office", "YouTube", "Độc hại" }, names);
        Assert.NotEmpty(result.Warnings);

        var chrome = result.Packages[0];
        Assert.Equal("chrome", chrome.Key);
        Assert.True(chrome.UsesWinGet);
        Assert.True(chrome.Recommended);
        Assert.True(chrome.MachineWide);
        Assert.Equal("Trình duyệt", chrome.Category);
        Assert.Equal("Trình duyệt web của Google", chrome.Description);
        Assert.Equal(new[] { "%ProgramFiles%\\Google\\Chrome\\Application\\chrome.exe" }, chrome.DetectPaths);
        Assert.True(chrome.IsAvailable);

        var rar = result.Packages[1];
        Assert.Equal("winrar", rar.Key);
        Assert.NotNull(rar.PageUrl);
        Assert.Equal("/S", rar.SilentArgs);
    }

    [Fact]
    public void Unavailable_apps_are_kept_with_a_reason()
    {
        var p = RemoteCatalog.Parse(AdminList).Packages.ToDictionary(x => x.Name);

        Assert.Equal("Chỉ cài được từ Microsoft Store", p["WhatsApp"].UnavailableReason);
        Assert.Contains("nhà phát hành", p["WPS Office"].UnavailableReason, StringComparison.Ordinal);
        Assert.Contains("tham số cài", p["Độc hại"].UnavailableReason, StringComparison.Ordinal);
        Assert.Null(p["Độc hại"].WinGetId);
        Assert.Equal("#3B5BDB", p["Độc hại"].Color);
        Assert.True(p["YouTube"].IsAvailable);
        Assert.Equal(InstallKind.Web, p["YouTube"].Kind);
    }

    [Fact]
    public void Variants_keep_only_https_and_allowed_kinds()
    {
        var office = RemoteCatalog.Parse(AdminList).Packages.Single(x => x.Name == "Microsoft Office");

        Assert.True(office.IsAvailable);
        Assert.Equal(2, office.Variants.Count);
        Assert.Equal(InstallKind.Exe, office.Variants[0].Kind);
        Assert.Equal(InstallKind.Web, office.Variants[1].Kind);
        Assert.All(office.Variants, v => Assert.Equal(Uri.UriSchemeHttps, v.Url.Scheme));
        Assert.Equal("m365", office.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("không phải json")]
    public void Broken_lists_give_no_apps_instead_of_crashing(string json)
    {
        var result = RemoteCatalog.Parse(json);

        Assert.Empty(result.Packages);
        Assert.NotEmpty(result.Warnings);
    }

    [Theory]
    [InlineData("Google Chrome", "Google.Chrome", "chrome")]
    [InlineData("Lối tắt Đặc biệt", null, "loi-tat-dac-biet")]
    [InlineData("K-Lite Codec Pack", "CodecGuide.K-LiteCodecPack.Standard", "k-lite-codec-pack")]
    public void Keys_are_stable_and_reuse_bundled_logos(string name, string? winget, string expected) =>
        Assert.Equal(expected, RemoteCatalog.KeyFor(name, winget));

    [Theory]
    [InlineData("https://dl.google.com/a.exe", true)]
    [InlineData("http://dl.google.com/a.exe", false)]
    [InlineData("https://user:pw@dl.google.com/a.exe", false)]
    [InlineData("https://dl.google.com/a b.exe", false)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    public void Only_plain_https_links_are_accepted(string url, bool ok) =>
        Assert.Equal(ok, RemoteCatalog.HttpsUrl(url) is not null);

    [Theory]
    [InlineData(@"%ProgramFiles%\UniKey\UniKeyNT.exe", true)]
    [InlineData(@"%LocalAppData%\Programs\Zalo\Zalo.exe", true)]
    [InlineData(@"C:\Windows\System32\cmd.exe", false)]
    [InlineData(@"%ProgramFiles%\..\Windows\x.exe", false)]
    [InlineData(@"%SystemRoot%\x.exe", false)]
    public void Detect_paths_stay_in_application_folders(string path, bool ok) =>
        Assert.Equal(ok, RemoteCatalog.IsAllowedDetectPath(path));

    [Fact]
    public void Trusted_publishers_match_exactly_after_normalising()
    {
        Assert.True(TrustedPublishers.IsTrusted("Microsoft Corporation"));
        Assert.True(TrustedPublishers.IsTrusted("  microsoft   CORPORATION. "));
        Assert.True(TrustedPublishers.IsTrusted("ESET, spol. s r.o."));
        Assert.True(TrustedPublishers.IsTrusted("Pham Kim Long"));
        Assert.False(TrustedPublishers.IsTrusted("Microsoft Corporation Fake"));
        Assert.False(TrustedPublishers.IsTrusted("Evil Google LLC"));
        Assert.False(TrustedPublishers.IsTrusted(null));
    }

    [Fact]
    public void Signer_name_is_read_from_quoted_subject()
    {
        const string Subject = "CN=\"ESET, spol. s r.o.\", O=\"ESET, spol. s r.o.\", L=Bratislava, C=SK";

        Assert.Equal("ESET, spol. s r.o.", Authenticode.Rdn(Subject, "O"));
        Assert.Equal("ESET, spol. s r.o.", Authenticode.Rdn(Subject, "CN"));
        Assert.Equal("SK", Authenticode.Rdn(Subject, "C"));
        Assert.Null(Authenticode.Rdn(Subject, "OU"));
        Assert.True(new SignatureCheck(true, "ESET, spol. s r.o.", null, "").IsTrusted);
        Assert.False(new SignatureCheck(false, "Microsoft Corporation", null, "").IsTrusted);
        Assert.False(new SignatureCheck(true, "Ai Đó Ltd", "Ai Đó Ltd", "").IsTrusted);
    }

    [Fact]
    public void Runtime_dll_signed_by_microsoft_is_trusted_and_plain_file_is_not()
    {
        var signed = Authenticode.Check(typeof(object).Assembly.Location);
        Assert.True(signed.IsValid, signed.Detail);
        Assert.True(signed.IsTrusted, signed.SignerText);

        var plain = Path.Combine(Path.GetTempPath(), "lmc-unsigned-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(plain, [0x4D, 0x5A, 0x90, 0x00]);
        try
        {
            var check = Authenticode.Check(plain);
            Assert.False(check.IsValid);
            Assert.False(check.IsTrusted);
        }
        finally
        {
            File.Delete(plain);
        }
    }

    [Fact]
    public void Links_found_on_page_are_resolved_and_must_be_https()
    {
        var page = new Uri("https://www.rarlab.com/download.htm");
        const string Html = """<a href="http://evil.test/x/winrar-x64-999vn.exe">x</a> <a href="/rar/winrar-x64-723vn.exe">Vietnamese</a>""";

        Assert.Null(LinkDownloader.FindLink(Html, @"http://evil\.test/x/winrar-x64-\d+vn\.exe", page));
        Assert.Equal(new Uri("https://www.rarlab.com/rar/winrar-x64-723vn.exe"), LinkDownloader.FindLink(Html, @"/rar/winrar-x64-\d+vn\.exe", page));
    }

    [Theory]
    [InlineData("https://x.test/files/ChromeSetup.exe", InstallKind.Exe, "ChromeSetup.exe")]
    [InlineData("https://x.test/download.aspx?id=1", InstallKind.Exe, "setup.exe")]
    [InlineData("https://x.test/ZoomInstallerFull.msi?archType=x64", InstallKind.Msi, "ZoomInstallerFull.msi")]
    [InlineData("https://x.test/evil.exe", InstallKind.Msi, "setup.msi")]
    [InlineData("https://x.test/..%5C..%5Cwindows.exe", InstallKind.Exe, "setup.exe")]
    public void Downloaded_file_names_are_safe(string url, InstallKind kind, string expected) =>
        Assert.Equal(expected, LinkDownloader.FileNameFor(new Uri(url), null, kind));

    [Fact]
    public void Installer_exit_codes_are_classified_without_false_success()
    {
        Assert.Equal(ExecuteOutcome.Completed, DirectInstallTask.Classify(0).Outcome);
        Assert.Equal(ExecuteOutcome.CompletedRebootRequired, DirectInstallTask.Classify(3010).Outcome);
        Assert.Equal(ExecuteOutcome.CompletedRebootRequired, DirectInstallTask.Classify(1641).Outcome);
        Assert.True(DirectInstallTask.Classify(1618).IsTransient);
        Assert.Equal(ExecuteOutcome.Failed, DirectInstallTask.Classify(1).Outcome);
        Assert.Equal(new[] { "/S", "/norestart" }, DirectInstallTask.SplitArgs(" /S  /norestart "));
        Assert.Empty(DirectInstallTask.SplitArgs("/S & calc"));
    }

    [Fact]
    public void WinGet_prefers_vietnamese_then_falls_back()
    {
        Assert.Equal(new (bool, string?)[] { (true, "vi-VN"), (true, null), (false, "vi-VN"), (false, null) }, WinGetInstallTask.Attempts(machineWide: true));
        Assert.Equal(new (bool, string?)[] { (false, "vi-VN"), (false, null) }, WinGetInstallTask.Attempts(machineWide: false));

        var args = WinGetClient.InstallArguments("RARLab.WinRAR", machineScope: true, "vi-VN");
        Assert.Equal(new[] { "--locale", "vi-VN", "--scope", "machine" }, args.Skip(args.Count - 4));
        Assert.DoesNotContain("--locale", WinGetClient.InstallArguments("RARLab.WinRAR", machineScope: false, "vi-VN; calc"));
    }

    [Fact]
    public async Task Greyed_app_reports_not_supported_with_its_reason()
    {
        var whatsapp = RemoteCatalog.Parse(AdminList).Packages.Single(x => x.Name == "WhatsApp");
        var task = new AppInstallTask(whatsapp, new WinGetClient(() => null));

        var result = await task.CheckPreconditionsAsync(new TaskContext(new InMemoryRunLogger(), 1), CancellationToken.None);

        Assert.Equal(PreconditionOutcome.NotSupported, result.Outcome);
        Assert.Equal("Chỉ cài được từ Microsoft Store", result.Reason);
        Assert.Empty(task.Descriptor.Validate());
        Assert.False(task.Descriptor.AllowForcedStop);
    }

    [Fact]
    public void Every_app_task_from_admin_list_has_a_valid_descriptor()
    {
        var tasks = RemoteCatalog.Parse(AdminList).Packages.Select(p => new AppInstallTask(p, new WinGetClient(() => null))).ToList();

        Assert.All(tasks, t => Assert.Empty(t.Descriptor.Validate()));
        Assert.Equal(tasks.Count, tasks.Select(t => t.Descriptor.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(RollbackSupport.NotPossible, tasks.Single(t => t.Package.Name == "Microsoft Office").Descriptor.Rollback);
    }
}
