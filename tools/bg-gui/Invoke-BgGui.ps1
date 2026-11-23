<#
.SYNOPSIS
  JumpDrillGui.exe を見えない別のデスクトップで起動し、UI Automation で操作して PrintWindow で撮る。
  ユーザーの画面には何も出ず、前面のウィンドウ（フォーカス）も変わらない。

.DESCRIPTION
  1. CreateDesktop で見えないデスクトップを作る（SwitchDesktop はしない）。
  2. 操作役の powershell.exe（bg-driver.ps1）を CreateProcess の lpDesktop でそのデスクトップに起動する。
     操作役が GUI を起動するので、GUI も同じデスクトップに出る。UI Automation は同じデスクトップの中でしか効かない。
     操作役と GUI（子・孫も）は Job Object に入れ、残りの確認と後片付けは Job で行う。
  3. その間ずっと 100 ms ごとに GetForegroundWindow を記録し、前面のウィンドウが変わったか・起動したプロセスが前面に出たかを出す。
  4. 終わったら Job の中のプロセスを普通に閉じ（だめなら止め）、デスクトップを閉じる。

  -Exe の exe が使う workspace（settings.txt）は exe の場所から決まる（Workspace.AppRoot）。
  ユーザーの workspace を使う exe（リポジトリの中・配布版）は既定で断る。-Build でリポジトリの外にビルドした exe を使う。
  試験用の workspace には目印のファイル（.bg-gui-test）を置き、目印の無い settings.txt がある workspace は断る。
  -SeedSettings は設定の複製を置く。出力先（out.dir）は試験用の workspace の下に書き換え、preview.dir は外す。
  元の設定・出力先・記録のフォルダ（recordsInstall の UserData\JumpDrill と JumpDrill）は自動で -WatchPaths に足す。
  それでも、生成・再作成・削除・MOD 導入・フォルダや URL を開くボタンは手順に入れない（ユーザーのファイルや見える画面に触れる）。

.EXAMPLE
  # メイン画面・メダル画面・プルダウンの変更・生成オプションのウィンドウ（キャンセル）を撮る
  .\tools\bg-gui\Invoke-BgGui.ps1 -Build -SeedSettings .\workspace\settings.txt -Steps @(
    'launch', 'shot=01-main',
    'click=メダル', 'win=*メダル*', 'waitnotext=*読み込んで*', 'shot=02-medal', 'close', 'win=JumpDrill v*',
    'combo=3:左', 'shot=03-main-combo-changed',
    'click=生成', 'win=生成オプション', 'shot=04-genopt', 'click=キャンセル', 'win=JumpDrill v*', 'shot=05-main-after-cancel',
    'close')
  手順の書き方は bg-driver.ps1 の先頭。結果は workspace\bg-gui\<日時>\ に PNG・run-log.txt・driver-log.txt・foreground.csv。
  run-log.txt の "foreground: ... samplesOwnedByLaunched=0" が、起動したプロセス（子・孫も）が一度も前面に出なかったことを示す。
  "leftover processes: 0" は Job の中に残ったプロセスが無いこと、"watched files changed: 0" は見張ったファイルが変わらなかったこと。
#>
param(
  [string]$Exe = "",
  [switch]$Build,
  [string]$BuildDir = "",
  [string]$SeedSettings = "",
  [Parameter(Mandatory = $true)][string[]]$Steps,
  [string]$OutDir = "",
  [string[]]$WatchPaths = @(),
  [int]$TimeoutSec = 180,
  [switch]$AllowRepoWorkspace,
  # 比べるための通常起動。見えるデスクトップ（WinSta0\Default）で動かすので、GUI のウィンドウが画面に出る
  [switch]$OnVisibleDesktop,
  # launch で待つメインのウィンドウの題名（ワイルドカード）。JumpDrill 以外の WinForms / WPF アプリにも使える
  [string]$MainTitle = "JumpDrill*"
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here "..\..")).Path
if (-not $OutDir) { $OutDir = Join-Path $repo ("workspace\bg-gui\" + (Get-Date -Format "yyyyMMdd-HHmmss")) }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$log = Join-Path $OutDir "run-log.txt"
Remove-Item $log, (Join-Path $OutDir "driver-log.txt") -ErrorAction SilentlyContinue
function L($t) { $line = "{0} {1}" -f (Get-Date -Format "HH:mm:ss.fff"), $t; $line | Out-File -Append -Encoding utf8 $log; Write-Host $line }

# ---- exe の用意 ----
if ($Build) {
  if (-not $BuildDir) { $BuildDir = Join-Path (Split-Path $repo -Parent) "_bggui\gui" }
  L "build -> $BuildDir"
  $b = & dotnet build (Join-Path $repo "src\JumpDrill.Gui\JumpDrill.Gui.csproj") -c Release -o $BuildDir -v q -nologo 2>&1
  if ($LASTEXITCODE -ne 0) { $b | ForEach-Object { L "  $_" }; throw "build failed" }
  $Exe = Join-Path $BuildDir "JumpDrillGui.exe"
}
if (-not $Exe -or -not (Test-Path $Exe)) { throw "exe not found: $Exe" }
$Exe = (Resolve-Path $Exe).Path
$exeItem = Get-Item $Exe
L ("exe: {0}  written: {1}" -f $Exe, $exeItem.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"))

# exe が使う workspace（Workspace.AppRoot と同じ探し方）
$appRoot = $exeItem.DirectoryName
$d = Get-Item $appRoot
for ($i = 0; $d -ne $null -and $i -lt 8; $i++) {
  if ((Test-Path (Join-Path $d.FullName "JumpDrill.sln")) -or (Test-Path (Join-Path $d.FullName "drill-gui.bat"))) { $appRoot = $d.FullName; break }
  $d = $d.Parent
}
$ws = Join-Path $appRoot "workspace"
$marker = Join-Path $ws ".bg-gui-test"
L "workspace of exe: $ws"
if (-not $AllowRepoWorkspace) {
  if (Test-Path (Join-Path $appRoot "JumpDrill.sln")) { throw "this exe uses the repository workspace ($ws). Build it elsewhere (-Build) or pass -AllowRepoWorkspace." }
  if ((Test-Path (Join-Path $ws "settings.txt")) -and -not (Test-Path $marker)) { throw "the workspace of this exe ($ws) has a settings.txt that this tool did not make. Use -Build." }
} elseif ($SeedSettings) { throw "-SeedSettings cannot be used with -AllowRepoWorkspace (it would overwrite the real settings.txt)." }

function Full($p) { if (Test-Path $p) { return (Resolve-Path $p).Path } else { return $p } }
$WatchPaths = @($WatchPaths | Where-Object { $_ } | ForEach-Object { Full $_ })
if ($SeedSettings) {
  New-Item -ItemType Directory -Force $ws | Out-Null
  Set-Content -Path $marker -Value "made by tools\bg-gui\Invoke-BgGui.ps1"
  $src = (Resolve-Path $SeedSettings).Path
  $dst = Join-Path $ws "settings.txt"
  $lines = @(Get-Content -Encoding utf8 $src)
  function Setting($k) { $m = $lines | Where-Object { $_ -like "$k=*" } | Select-Object -First 1; if ($m) { return $m.Substring($k.Length + 1) } else { return "" } }
  $origOut = Setting "out.dir"; $records = Setting "recordsInstall"
  $lines = @($lines | Where-Object { $_ -notmatch '^(paths\.workspace|out\.dir|preview\.dir)=' })
  $lines += "paths.workspace=$ws"
  $lines += "out.dir=" + (Join-Path $ws "out")
  [System.IO.File]::WriteAllLines($dst, [string[]]$lines, (New-Object System.Text.UTF8Encoding $false))
  L "seeded settings: $dst (copy of $src; out.dir -> $ws\out, preview.dir removed)"
  # 元の設定・出力先・記録のフォルダは自動で見張る
  $auto = @($src, $origOut)
  if ($records) { $auto += (Join-Path $records "UserData\JumpDrill"); $auto += (Join-Path $records "JumpDrill") }
  foreach ($w in $auto) { if ($w -and (Test-Path $w)) { $f = Full $w; if ($WatchPaths -notcontains $f) { $WatchPaths += $f } } }
}
L ("watching: " + ($WatchPaths -join "; "))

# ---- 見張るファイルのハッシュ（前） ----
function HashAll($paths) {
  $rows = @("(sentinel)")   # 空のときも Compare-Object が止まらないように
  foreach ($p in $paths) {
    if (-not (Test-Path $p)) { $rows += "$p,(missing)"; continue }
    Get-ChildItem $p -Recurse -File -Force | ForEach-Object { $rows += ("{0},{1},{2}" -f $_.FullName, $_.Length, $_.LastWriteTimeUtc.Ticks) }
  }
  return ,$rows
}
$before = HashAll $WatchPaths

if (-not ([System.Management.Automation.PSTypeName]'BgDesk').Type) { Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $here "BgDesk.cs"))) }

$hDesk = [IntPtr]::Zero
$job = $null
$timedOut = $false
$started = Get-Date
$fgStart = [BgDesk]::GetForegroundWindow()
try {
  if ($OnVisibleDesktop) {
    $deskName = "Default"
    L "WARNING: running on the visible desktop; the GUI window will be shown on the screen"
  } else {
    $deskName = "jdbg-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
    # GENERIC_ALL。SwitchDesktop はしないので、このデスクトップは画面に出ない
    $hDesk = [BgDesk]::CreateDesktop($deskName, [IntPtr]::Zero, [IntPtr]::Zero, 0, 0x10000000, [IntPtr]::Zero)
    if ($hDesk -eq [IntPtr]::Zero) { throw "CreateDesktop failed: $([System.Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
    L "desktop created: WinSta0\$deskName"
  }

  $stepsFile = Join-Path $OutDir "steps.txt"
  [System.IO.File]::WriteAllLines($stepsFile, [string[]]$Steps, (New-Object System.Text.UTF8Encoding $true))
  $pidFile = Join-Path $OutDir "gui-pid.txt"
  Remove-Item $pidFile -ErrorAction SilentlyContinue
  $driver = Join-Path $here "bg-driver.ps1"
  $ps = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
  $cmd = "`"$ps`" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$driver`" -Exe `"$Exe`" -StepsFile `"$stepsFile`" -OutDir `"$OutDir`" -PidFile `"$pidFile`" -MainTitle `"$MainTitle`""

  [BgDesk]::StartWatch(100)
  $started = Get-Date
  # 操作役と、操作役が起動する GUI（子・孫も）は同じ Job に入る
  $job = [BgDesk]::Start("WinSta0\$deskName", $cmd, $OutDir)
  [BgDesk]::WatchJob($job)
  L "driver started pid=$($job.Pid) at $($started.ToString('yyyy-MM-dd HH:mm:ss.fff')) on WinSta0\$deskName"
  if (-not [BgDesk]::Wait($job, $TimeoutSec * 1000)) { $timedOut = $true }
  L "driver finished (timedOut=$timedOut, exit=$([BgDesk]::ExitCode($job))) after $([int]((Get-Date) - $started).TotalSeconds) s"
} finally {
  # ---- 後片付け：Job の中（自分が起動したものだけ）を、まず普通に閉じ、だめなら止める ----
  if ($job) {
    if (-not [BgDesk]::WaitAll($job, 3000)) {
      if ($hDesk -ne [IntPtr]::Zero) { L "WM_CLOSE sent to windows left on the desktop: $([BgDesk]::CloseWindows($hDesk, $job))" }
      if (-not [BgDesk]::WaitAll($job, 10000)) {
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

  # ---- フォーカスの記録 ----
  $samples = @([BgDesk]::Samples)
  $names = @{}
  $rows = @(foreach ($s in $samples) {
    if (-not $names.ContainsKey($s.Pid)) { $n = "?"; try { $n = (Get-Process -Id $s.Pid -ErrorAction Stop).ProcessName } catch {}; $names[$s.Pid] = $n }
    "{0},{1},{2},{3},{4}" -f $s.At.ToString("HH:mm:ss.fff"), $s.Hwnd, $s.Pid, $names[$s.Pid], $s.Launched
  })
  @("time,hwnd,pid,process,launchedByThisTool") + $rows | Set-Content -Encoding utf8 (Join-Path $OutDir "foreground.csv")
  $changes = 0; for ($i = 1; $i -lt $samples.Count; $i++) { if ($samples[$i].Hwnd -ne $samples[$i - 1].Hwnd) { $changes++ } }
  $mine = @($samples | Where-Object { $_.Launched }).Count
  $distinct = @($samples | ForEach-Object { $_.Hwnd } | Sort-Object -Unique)
  L ("foreground: samples={0} changes={1} distinctWindows={2} samplesOwnedByLaunched={3} start=0x{4:X}" -f $samples.Count, $changes, $distinct.Count, $mine, $fgStart.ToInt64())

  # ---- 見張るファイルのハッシュ（後） ----
  if ($WatchPaths.Count -gt 0) {
    $after = HashAll $WatchPaths
    $diff = @(Compare-Object $before $after | ForEach-Object { $_.InputObject.Split(',')[0] } | Sort-Object -Unique)
    L "watched files changed: $($diff.Count)"
    $diff | ForEach-Object { L "  changed: $_" }
  }
  $dlog = Join-Path $OutDir "driver-log.txt"
  if (Test-Path $dlog) { L "---- driver log ----"; Get-Content -Encoding utf8 $dlog | ForEach-Object { L $_ } }
  L "out: $OutDir"
}
