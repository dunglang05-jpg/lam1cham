namespace LamMotCham.Windows.Probe;

/// <summary>
/// Script PowerShell CHỈ ĐỌC, đóng gói trong ứng dụng. Chia thành các nhóm chạy song song (mỗi nhóm một tiến trình)
/// để rút ngắn thời gian thu thập; kết quả các nhóm được gộp lại. Lỗi của phần nào ghi vào <c>errors</c>, không làm hỏng phần khác.
/// Không đọc khóa sản phẩm, khóa khôi phục BitLocker hay mật khẩu.
/// </summary>
internal static class InventoryScript
{
    private const string Header = """
        $r = [ordered]@{}
        $errs = New-Object System.Collections.ArrayList
        function Sec([string]$name, [scriptblock]$body) {
          try { & $body } catch { [void]$errs.Add("${name}: $($_.Exception.Message)") }
        }
        function D($v) { if ($null -eq $v) { $null } else { ([datetime]$v).ToUniversalTime().ToString('o') } }
        """;

    private const string Footer = """
        $r.errors = @($errs)
        $r | ConvertTo-Json -Depth 6 -Compress
        """;

    private const string CauHinh = """
        Sec 'system' {
          $cs = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop
          $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
          $cv = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
          $r.computerName = [string]$env:COMPUTERNAME
          $r.manufacturer = [string]$cs.Manufacturer
          $r.model = [string]$cs.Model
          $r.totalRamBytes = [int64]$cs.TotalPhysicalMemory
          $r.osCaption = [string]$os.Caption
          $r.osBuild = [int]$os.BuildNumber
          $r.osDisplayVersion = [string]$cv.DisplayVersion
          $r.osUbr = [int]$cv.UBR
          $r.lastBoot = D $os.LastBootUpTime
          $r.cpu = [string](Get-CimInstance Win32_Processor | Select-Object -First 1).Name
          $r.serialNumber = [string](Get-CimInstance Win32_BIOS).SerialNumber
          $r.systemDrive = [string]$env:SystemDrive
        }

        Sec 'disks' {
          $r.volumes = @(Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' -ErrorAction Stop | ForEach-Object {
            [ordered]@{ drive = [string]$_.DeviceID; size = [int64]$_.Size; free = [int64]$_.FreeSpace; fileSystem = [string]$_.FileSystem; label = [string]$_.VolumeName } })
        }

        Sec 'bitlocker' {
          $r.bitlocker = @(Get-CimInstance -Namespace 'root\cimv2\Security\MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume -ErrorAction Stop | ForEach-Object {
            $cv = $null; $pc = $null
            try { $cs = Invoke-CimMethod -InputObject $_ -MethodName GetConversionStatus -ErrorAction Stop; $cv = [int]$cs.ConversionStatus; $pc = [int][math]::Round([double]$cs.EncryptionPercentage) } catch { }
            [ordered]@{ drive = [string]$_.DriveLetter; protection = [int]$_.ProtectionStatus; conversion = $cv; percent = $pc } })
        }

        Sec 'reboot' {
          $p = @()
          if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') { $p += 'ComponentBasedServicing' }
          if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') { $p += 'WindowsUpdate' }
          $r.pendingReboot = @($p)
        }

        Sec 'battery' {
          $r.battery = @(Get-CimInstance Win32_Battery -ErrorAction Stop | ForEach-Object {
            [ordered]@{ name = [string]$_.Name; charge = [int]$_.EstimatedChargeRemaining; status = [int]$_.BatteryStatus } })
          try {
            $design = (Get-CimInstance -Namespace 'root\WMI' -ClassName BatteryStaticData -ErrorAction Stop | Select-Object -First 1).DesignedCapacity
            $full = (Get-CimInstance -Namespace 'root\WMI' -ClassName BatteryFullChargedCapacity -ErrorAction Stop | Select-Object -First 1).FullChargedCapacity
            if ($design -and $full) { $r.batteryCapacity = [ordered]@{ design = [int64]$design; full = [int64]$full } }
          } catch { }
          # Một số máy (Dell Inspiron 15 3530, 02/11/2026) không có lớp WMI trên: đọc từ báo cáo pin của powercfg.
          if (-not $r.batteryCapacity) {
            $x = Join-Path $env:TEMP ('l1c-pin-' + [guid]::NewGuid().ToString('N') + '.xml')
            try {
              $null = & "$env:SystemRoot\System32\powercfg.exe" /batteryreport /xml /output $x 2>$null
              $code = $LASTEXITCODE
              if (Test-Path $x) {
                $doc = [xml](Get-Content -LiteralPath $x -Raw)
                $b = @($doc.GetElementsByTagName('Battery'))[0]
                $dc = if ($b) { [int64]('0' + $b['DesignCapacity'].InnerText) } else { 0 }
                $fc = if ($b) { [int64]('0' + $b['FullChargeCapacity'].InnerText) } else { 0 }
                if ($dc -gt 0 -and $fc -gt 0) {
                  $r.batteryCapacity = [ordered]@{ design = $dc; full = $fc }
                } else {
                  [void]$errs.Add("batteryCapacity: báo cáo pin không có dung lượng (thiết kế $dc, sạc đầy $fc)")
                }
              } else {
                [void]$errs.Add("batteryCapacity: powercfg không tạo được báo cáo pin (mã $code)")
              }
            } catch {
              [void]$errs.Add("batteryCapacity: $($_.Exception.Message)")
            } finally { Remove-Item -LiteralPath $x -Force -ErrorAction SilentlyContinue }
          }
        }

        Sec 'brightness' {
          $r.brightness = @(Get-CimInstance -Namespace 'root\WMI' -ClassName WmiMonitorBrightness -ErrorAction Stop | ForEach-Object { [int]$_.CurrentBrightness })
        }

        Sec 'users' {
          $r.localUsersEnabled = @(Get-LocalUser -ErrorAction Stop | Where-Object { $_.Enabled }).Count
        }

        Sec 'input' {
          $r.keyboards = @(Get-CimInstance Win32_Keyboard -ErrorAction Stop | ForEach-Object { [ordered]@{ name = [string]$_.Name; status = [string]$_.Status } })
          $r.pointing = @(Get-CimInstance Win32_PointingDevice -ErrorAction Stop | ForEach-Object { [ordered]@{ name = [string]$_.Name; status = [string]$_.Status } })
        }

        Sec 'sound' {
          $r.sound = @(Get-CimInstance Win32_SoundDevice -ErrorAction Stop | ForEach-Object { [ordered]@{ name = [string]$_.Name; status = [string]$_.Status } })
        }
        """;

    private const string BanQuyenCapNhat = """
        Sec 'activation' {
          $p = Get-CimInstance SoftwareLicensingProduct -Filter "ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL" -ErrorAction Stop | Select-Object -First 1
          if ($p) { $r.activation = [ordered]@{ name = [string]$p.Name; status = [int]$p.LicenseStatus } }
        }

        Sec 'update' {
          $svc = Get-Service wuauserv -ErrorAction Stop
          $u = [ordered]@{ startType = [string]$svc.StartType; status = [string]$svc.Status; lastSuccess = $null; lastHotfix = $null }
          try { $au = New-Object -ComObject Microsoft.Update.AutoUpdate; $u.lastSuccess = D $au.Results.LastInstallationSuccessDate } catch { }
          $hf = Get-HotFix -ErrorAction SilentlyContinue | Where-Object { $_.InstalledOn } | Sort-Object InstalledOn -Descending | Select-Object -First 1
          if ($hf) { $u.lastHotfix = D $hf.InstalledOn }
          $r.update = $u
        }
        """;

    private const string BaoMatMang = """
        Sec 'defender' {
          $m = Get-MpComputerStatus -ErrorAction Stop
          $r.defender = [ordered]@{ amService = [bool]$m.AMServiceEnabled; antivirus = [bool]$m.AntivirusEnabled; realTime = [bool]$m.RealTimeProtectionEnabled; signatureUpdated = (D $m.AntivirusSignatureLastUpdated) }
        }

        Sec 'antivirus' {
          $r.antivirus = @(Get-CimInstance -Namespace 'root\SecurityCenter2' -ClassName AntiVirusProduct -ErrorAction Stop | ForEach-Object {
            [ordered]@{ name = [string]$_.displayName; state = [int64]$_.productState } })
        }

        Sec 'firewall' {
          $r.firewall = @(Get-NetFirewallProfile -ErrorAction Stop | ForEach-Object {
            [ordered]@{ name = [string]$_.Name; enabled = ([string]$_.Enabled -eq 'True') } })
        }

        Sec 'network' {
          $r.adapters = @(Get-NetAdapter -Physical -ErrorAction Stop | ForEach-Object {
            [ordered]@{ name = [string]$_.Name; desc = [string]$_.InterfaceDescription; media = [string]$_.PhysicalMediaType; status = [string]$_.Status } })
        }

        Sec 'physicalDisks' {
          $r.physicalDisks = @(Get-PhysicalDisk -ErrorAction Stop | ForEach-Object {
            [ordered]@{ name = [string]$_.FriendlyName; media = [string]$_.MediaType; bus = [string]$_.BusType; size = [int64]$_.Size; health = [string]$_.HealthStatus } })
        }
        """;

    private const string ThietBiUngDung = """
        Sec 'devices' {
          # Lọc ngay trong WQL thay vì đọc toàn bộ Win32_PnPEntity (nhanh hơn nhiều trên máy chậm).
          $r.devices = @(Get-CimInstance Win32_PnPEntity -Filter "PNPClass='Bluetooth' OR PNPClass='Camera' OR PNPClass='Image'" -ErrorAction Stop | Where-Object { $_.Present } | ForEach-Object {
            [ordered]@{ cls = [string]$_.PNPClass; name = [string]$_.Name; err = [int]$_.ConfigManagerErrorCode } })
          $r.problemDevices = @(Get-CimInstance Win32_PnPEntity -Filter "ConfigManagerErrorCode <> 0" -ErrorAction Stop | Where-Object { $_.Present } | Select-Object -First 25 | ForEach-Object {
            [ordered]@{ cls = [string]$_.PNPClass; name = [string]$_.Name; err = [int]$_.ConfigManagerErrorCode } })
        }

        Sec 'startup' {
          $r.startup = @(Get-CimInstance Win32_StartupCommand -ErrorAction Stop | ForEach-Object { [ordered]@{ name = [string]$_.Name; location = [string]$_.Location } })
        }

        Sec 'apps' {
          $keys = @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
                    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
                    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*')
          $r.apps = @(Get-ItemProperty $keys -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -and -not $_.SystemComponent } | ForEach-Object {
            [ordered]@{ name = [string]$_.DisplayName; publisher = [string]$_.Publisher } })
        }
        """;

    /// <summary>Các nhóm chạy song song: tên nhóm và script đầy đủ (đầu + các phần + cuối).</summary>
    public static IReadOnlyList<(string Name, string Script)> Groups { get; } =
    [
        ("cauHinh", Header + "\n" + CauHinh + "\n" + Footer),
        ("banQuyenCapNhat", Header + "\n" + BanQuyenCapNhat + "\n" + Footer),
        ("baoMatMang", Header + "\n" + BaoMatMang + "\n" + Footer),
        ("thietBiUngDung", Header + "\n" + ThietBiUngDung + "\n" + Footer),
    ];
}
