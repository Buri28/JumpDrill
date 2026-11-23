<#
.SYNOPSIS
  任意のコマンドを見えない別のデスクトップで起動し、終わるまで（または時間切れまで）待つ。
  その間 100 ms ごとに前面のウィンドウを記録する。Beat Saber の fpfc をプローブ（ログ・カメラ撮影）で確かめるときに使う。
  起動したプロセス（子・孫も）は Job Object に入れ、時間切れのときは WM_CLOSE を送り、だめなら Job ごと止める。
  呼ぶ側の時間切れ（ツールの実行時間の上限）が -TimeoutSec より短いと、後片付けの前に止められるので、長く取るか後ろで動かす。

.EXAMPLE
  $env:SteamAppId = "620980"
  .\tools\bg-gui\Invoke-Hidden.ps1 -FilePath "<Beat Saber のフォルダ>\Beat Saber.exe" -Arguments '--no-yeet fpfc --myprobe' `
    -WorkingDirectory "<Beat Saber のフォルダ>" -TimeoutSec 300 -OutDir .\workspace\bg-hidden
#>
param(
  [Parameter(Mandatory = $true)][string]$FilePath,
  [string]$Arguments = "",
  [string]$WorkingDirectory = "",
  [int]$TimeoutSec = 300,
  [string]$OutDir = ""
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here "..\..")).Path
if (-not $OutDir) { $OutDir = Join-Path $repo ("workspace\bg-hidden\" + (Get-Date -Format "yyyyMMdd-HHmmss")) }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$log = Join-Path $OutDir "run-log.txt"
Remove-Item $log -ErrorAction SilentlyContinue
function L($t) { $line = "{0} {1}" -f (Get-Date -Format "HH:mm:ss.fff"), $t; $line | Out-File -Append -Encoding utf8 $log; Write-Host $line }
if (-not ([System.Management.Automation.PSTypeName]'BgDesk').Type) { Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $here "BgDesk.cs"))) }

$FilePath = (Resolve-Path $FilePath).Path
if (-not $WorkingDirectory) { $WorkingDirectory = Split-Path $FilePath }
L ("exe: {0}  written: {1}" -f $FilePath, (Get-Item $FilePath).LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"))

$hDesk = [IntPtr]::Zero
$job = $null
$fgStart = [BgDesk]::GetForegroundWindow()
$started = Get-Date
try {
  $deskName = "jdbg-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
  $hDesk = [BgDesk]::CreateDesktop($deskName, [IntPtr]::Zero, [IntPtr]::Zero, 0, 0x10000000, [IntPtr]::Zero)
  if ($hDesk -eq [IntPtr]::Zero) { throw "CreateDesktop failed: $([System.Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
  L "desktop created: WinSta0\$deskName"

  [BgDesk]::StartWatch(100)
  $started = Get-Date
  $job = [BgDesk]::Start("WinSta0\$deskName", "`"$FilePath`" $Arguments", $WorkingDirectory, 0)
  [BgDesk]::WatchJob($job)
  L "started pid=$($job.Pid) at $($started.ToString('yyyy-MM-dd HH:mm:ss.fff')) args: $Arguments"
  if ([BgDesk]::Wait($job, $TimeoutSec * 1000)) { L "exited by itself (code $([BgDesk]::ExitCode($job))) after $([int]((Get-Date) - $started).TotalSeconds) s" }
  else { L "timeout after $TimeoutSec s" }
} finally {
  if ($job) {
    # 子（クラッシュハンドラーなど）も含めて、まずウィンドウに WM_CLOSE を送り、だめなら Job ごと止める
    if (-not [BgDesk]::WaitAll($job, 5000)) {
      if ($hDesk -ne [IntPtr]::Zero) { L "WM_CLOSE sent to windows on the desktop: $([BgDesk]::CloseWindows($hDesk, $job))" }
      if (-not [BgDesk]::WaitAll($job, 15000)) {
        $names = @(Get-Process | Where-Object { [BgDesk]::InJob($job, [uint32]$_.Id) } | ForEach-Object { "$($_.ProcessName)($($_.Id))" }) -join ", "
        L "processes still running in the job: $([BgDesk]::Active($job)) [$names]; terminating the job"
        [BgDesk]::Terminate($job); [BgDesk]::WaitAll($job, 5000) | Out-Null
      }
    }
    L "leftover processes: $([BgDesk]::Active($job))"
  }
  [BgDesk]::StopWatch()
  if ($job) { [BgDesk]::Close($job) }
  if ($hDesk -ne [IntPtr]::Zero) { [BgDesk]::CloseDesktop($hDesk) | Out-Null; L "desktop closed" }

  $samples = @([BgDesk]::Samples)
  $names = @{}
  $rows = @(foreach ($s in $samples) {
    if (-not $names.ContainsKey($s.Pid)) { $n = "?"; try { $n = (Get-Process -Id $s.Pid -ErrorAction Stop).ProcessName } catch {}; $names[$s.Pid] = $n }
    "{0},{1},{2},{3},{4}" -f $s.At.ToString("HH:mm:ss.fff"), $s.Hwnd, $s.Pid, $names[$s.Pid], $s.Launched
  })
  @("time,hwnd,pid,process,launchedByThisTool") + $rows | Set-Content -Encoding utf8 (Join-Path $OutDir "foreground.csv")
  $changes = 0; for ($i = 1; $i -lt $samples.Count; $i++) { if ($samples[$i].Hwnd -ne $samples[$i - 1].Hwnd) { $changes++ } }
  $mine = @($samples | Where-Object { $_.Launched }).Count
  L ("foreground: samples={0} changes={1} samplesOwnedByLaunched={2} start=0x{3:X}" -f $samples.Count, $changes, $mine, $fgStart.ToInt64())
}
