using System.Diagnostics;
using System.IO;
using System.Text;

namespace LocalVoice.App.Services;

/// <summary>
/// Launches a short hidden Windows PowerShell helper that waits for this app to exit,
/// runs the verified per-user installer silently, then restarts LocalVoice.
/// Using an external shell avoids locking LocalVoice.exe while it is being replaced.
/// </summary>
public static class UpdateInstallerHelper
{
    public static Process Launch(string installerPath, string targetExe, int parentProcessId, string logDirectory, string? fallbackExe = null)
    {
        if (!File.Exists(installerPath)) throw new FileNotFoundException("Скачанный установщик не найден.", installerPath);
        Directory.CreateDirectory(logDirectory);
        var targetDir = Path.GetDirectoryName(targetExe) ?? AppContext.BaseDirectory;
        var failureExe = File.Exists(fallbackExe) ? fallbackExe! : (File.Exists(targetExe) ? targetExe : null);
        var logPath = Path.Combine(logDirectory, "update.log");
        var script = BuildScript(installerPath, targetExe, targetDir, parentProcessId, logPath, failureExe);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) powershell = "powershell.exe";
        var psi = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Path.GetTempPath()
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(encoded);
        return Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить помощник обновления.");
    }

    private static string BuildScript(string installer, string target, string targetDir, int pid, string logPath, string? failureExe)
    {
        static string Q(string value) => "'" + value.Replace("'", "''") + "'";
        var installerArgs = new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/DIR=" + targetDir };
        var argLine = string.Join(" ", installerArgs.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a));
        return $$"""
$ErrorActionPreference = 'Stop'
$log = {{Q(logPath)}}
function Write-UpdateLog([string]$Message) {
    try { Add-Content -LiteralPath $log -Value ((Get-Date).ToString('o') + ' ' + $Message) } catch { }
}
try {
    Write-UpdateLog 'Waiting for LocalVoice to exit'
    try { Wait-Process -Id {{pid}} -Timeout 60 -ErrorAction Stop } catch { }
    Start-Sleep -Milliseconds 750
    Write-UpdateLog 'Running verified installer'
    $installerArgLine = {{Q(argLine)}}
    $p = Start-Process -FilePath {{Q(installer)}} -ArgumentList $installerArgLine -Wait -PassThru
    Write-UpdateLog ('Installer exit code ' + $p.ExitCode)
    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) { throw ('Installer failed with exit code ' + $p.ExitCode) }
    Start-Process -FilePath {{Q(target)}} -ArgumentList '--minimized' -WorkingDirectory {{Q(targetDir)}}
    Remove-Item -LiteralPath {{Q(installer)}} -Force -ErrorAction SilentlyContinue
    Write-UpdateLog 'Update complete'
} catch {
    Write-UpdateLog ('ERROR ' + $_.Exception.Message)
    try { Start-Process -FilePath {{Q(failureExe ?? target)}} -ArgumentList '--update-failed' } catch { }
    exit 1
}
""";
    }
}
