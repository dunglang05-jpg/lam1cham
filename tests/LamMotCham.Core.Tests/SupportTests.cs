using LamMotCham.Core.Logging;
using LamMotCham.Core.Network;
using LamMotCham.Core.Orchestration;
using LamMotCham.Core.Reporting;
using LamMotCham.Core.Resources;
using LamMotCham.Core.Tasks;
using Xunit;

namespace LamMotCham.Core.Tests;

public sealed class RedactorTests
{
    [Fact]
    public void Removes_bitlocker_recovery_keys()
    {
        var text = Redactor.Redact("Khóa: 123456-234567-345678-456789-567890-678901-789012-890123 hết");
        Assert.DoesNotContain("123456", text);
        Assert.Contains(Redactor.Placeholder, text);
    }

    [Fact]
    public void Removes_product_keys_tokens_and_passwords()
    {
        var text = Redactor.Redact("key ABCDE-12345-FGHIJ-67890-KLMNO; password=\"p@ss w0rd\"; token: abc.def; Authorization=xyz; Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.sig");
        Assert.DoesNotContain("ABCDE-12345", text);
        Assert.DoesNotContain("p@ss", text);
        Assert.DoesNotContain("abc.def", text);
        Assert.DoesNotContain("xyz", text);
        Assert.DoesNotContain("eyJhbGci", text);
    }

    [Fact]
    public void Hides_user_name_in_profile_paths()
    {
        var text = Redactor.Redact(@"Đã ghi C:\Users\nguyenvana\AppData\Local\Temp\x.log");
        Assert.DoesNotContain("nguyenvana", text);
        Assert.Contains(@"C:\Users\[USER]\AppData", text);
    }

    [Fact]
    public void Leaves_normal_text_unchanged()
    {
        const string text = "Cài Google.Chrome thành công, mã thoát 0x8A150061.";
        Assert.Equal(text, Redactor.Redact(text));
    }
}

public sealed class RotatingFileLoggerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lmc-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Rotates_and_keeps_bounded_number_of_files()
    {
        var logger = new RotatingFileLogger(_dir, "t", maxFileBytes: 2048, maxFiles: 3);
        for (var i = 0; i < 200; i++)
        {
            logger.Info($"Dòng log số {i} với nội dung đủ dài để làm đầy file nhanh hơn.");
        }

        var files = Directory.GetFiles(_dir, "t*.log");
        Assert.Equal(3, files.Length);
        Assert.True(files.All(f => new FileInfo(f).Length <= 2048));
    }

    [Fact]
    public void Writes_redacted_json_lines()
    {
        var logger = new RotatingFileLogger(_dir, "t");
        logger.Error("password=Bimat123", "task.x");

        var content = File.ReadAllText(logger.CurrentFilePath);
        Assert.DoesNotContain("Bimat123", content);
        Assert.Contains("\"task\":\"task.x\"", content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}

public sealed class ResourceGovernorTests
{
    [Fact]
    public void Pauses_below_threshold_and_resumes_only_above_resume_threshold_after_cooldown()
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-10-18T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var monitor = new QueueMonitor(50, 10, 18, 25, 25);
        var governor = new ResourceGovernor(monitor, new ResourceGovernorOptions { Cooldown = TimeSpan.FromSeconds(10) }, time);

        Assert.True(governor.Evaluate().CanStartNewTask);   // 50%
        Assert.False(governor.Evaluate().CanStartNewTask);  // 10% → tạm dừng
        Assert.False(governor.Evaluate().CanStartNewTask);  // 18%: trên ngưỡng dừng nhưng dưới ngưỡng chạy tiếp
        Assert.False(governor.Evaluate().CanStartNewTask);  // 25% nhưng chưa hết cooldown
        time.Advance(TimeSpan.FromSeconds(11));
        Assert.True(governor.Evaluate().CanStartNewTask);   // 25% và đã qua cooldown
    }

    [Fact]
    public void Unreadable_memory_does_not_block_work()
    {
        var governor = new ResourceGovernor(new QueueMonitor([null]));
        var decision = governor.Evaluate();

        Assert.True(decision.CanStartNewTask);
        Assert.NotNull(decision.Reason);
    }

    [Fact]
    public void Rejects_thresholds_without_hysteresis()
    {
        Assert.Throws<ArgumentException>(() => _ = new ResourceGovernor(
            new QueueMonitor(50),
            new ResourceGovernorOptions { PauseBelowAvailableMemoryPercent = 20, ResumeAboveAvailableMemoryPercent = 20 }));
    }
}

public sealed class RetryPolicyTests
{
    [Fact]
    public void Backoff_grows_and_is_capped()
    {
        var p = new RetryPolicy { MaxAttempts = 5, InitialDelay = TimeSpan.FromSeconds(10), BackoffFactor = 3, MaxDelay = TimeSpan.FromSeconds(60) };

        Assert.Equal(TimeSpan.FromSeconds(10), p.GetDelayBeforeAttempt(2));
        Assert.Equal(TimeSpan.FromSeconds(30), p.GetDelayBeforeAttempt(3));
        Assert.Equal(TimeSpan.FromSeconds(60), p.GetDelayBeforeAttempt(4));
    }

    [Fact]
    public void Too_many_attempts_is_invalid()
    {
        Assert.NotEmpty(new RetryPolicy { MaxAttempts = 50 }.Validate());
        Assert.Empty(RetryPolicy.None.Validate());
    }
}

public sealed class SntpTests
{
    [Fact]
    public void Computes_offset_from_server_timestamps()
    {
        var t1 = new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);
        var t4 = t1.AddMilliseconds(100);
        var serverTime = t1.AddSeconds(5).AddMilliseconds(50);
        var packet = new byte[48];
        packet[0] = 0x24; // LI 0, VN 4, Mode 4 (server)
        packet[1] = 2;    // stratum
        WriteTimestamp(packet, 32, serverTime);
        WriteTimestamp(packet, 40, serverTime);

        var offset = SntpClient.ComputeOffset(packet, t1, t4);

        Assert.NotNull(offset);
        Assert.InRange(offset!.Value.TotalMilliseconds, 4990, 5010);
    }

    [Fact]
    public void Rejects_invalid_packets()
    {
        var packet = new byte[48];
        packet[0] = 0x23; // mode 3 (client) không hợp lệ cho phản hồi
        Assert.Null(SntpClient.ComputeOffset(packet, DateTime.UtcNow, DateTime.UtcNow));
    }

    private static void WriteTimestamp(byte[] buffer, int offset, DateTime time)
    {
        var span = time - new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var seconds = (uint)Math.Floor(span.TotalSeconds);
        var fraction = (uint)((span.TotalSeconds - seconds) * 4294967296.0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset, 4), seconds);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset + 4, 4), fraction);
    }
}

public sealed class ReportWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lmc-report-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Writes_json_and_escaped_html_without_secrets()
    {
        var now = DateTimeOffset.UtcNow;
        var report = new RunReport
        {
            StartedAt = now,
            FinishedAt = now.AddMinutes(1),
            Tasks =
            [
                new TaskOutcome
                {
                    TaskId = "a", DisplayName = "<script>x</script>", Kind = TaskKind.Check, Risk = RiskLevel.ReadOnly,
                    State = TaskState.Failed, Reason = "token=abc123", Attempts = 1,
                },
            ],
        };
        var files = ReportWriter.Write(_dir, report, [new("Tên máy", "PC-01")], "1.0.0", TimeZoneInfo.Utc);

        var html = File.ReadAllText(files.HtmlPath);
        var json = File.ReadAllText(files.JsonPath);
        Assert.DoesNotContain("<script>x", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("abc123", html);
        Assert.DoesNotContain("abc123", json);
        Assert.Contains("PC-01", json);
    }

    [Fact]
    public void Two_reports_in_the_same_second_do_not_overwrite_each_other()
    {
        // BC-002 P3-3: tên file theo giây từng làm lượt sau ghi đè báo cáo lượt trước.
        var now = new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero);
        var report = new RunReport { StartedAt = now, FinishedAt = now, Tasks = [] };

        var first = ReportWriter.Write(_dir, report, [], "1.0.0", TimeZoneInfo.Utc);
        var second = ReportWriter.Write(_dir, report, [], "1.0.0", TimeZoneInfo.Utc);

        Assert.NotEqual(first.HtmlPath, second.HtmlPath);
        Assert.NotEqual(first.JsonPath, second.JsonPath);
        Assert.True(File.Exists(first.HtmlPath) && File.Exists(second.HtmlPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
