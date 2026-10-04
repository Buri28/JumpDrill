<#
    JumpDrill 配布版のビルド

        .\build-release.ps1                  # version.json のバージョンでビルド
        .\build-release.ps1 -Version 0.2.0   # version.json を書き換えてからビルド

    できるもの:
        release\JumpDrill\JumpDrillGui.exe   GUI（.NET ランタイム同梱の単一 exe）
        release\JumpDrill\Update.exe         更新ツール（.NET Framework 4.8）
        release\JumpDrill\version.json
        release\JumpDrill-v<版>.zip          GitHub Release に添付する zip
        release\JumpDrillMod-v<版>-bs<ゲームの版>.zip
                                             MOD（Plugins\JumpDrillMod.dll）。ゲームの版ごとに1つ。
                                             GUI の［MOD を入れる］は、入れる先のゲームの版に合うものをここから取る

    Update.exe は Release に添付された JumpDrill-*.zip を探して差し替えるので、
    zip の名前は変えないこと。

    MOD は deploy_instances.json に並べたゲームの版ごとにビルドする（無ければ BeatSaberPath の1つだけ）。
    MOD の manifest.json の version も version.json に揃え、gameVersion はビルドごとに差し替える。
    GUI はアプリと同じ版のリリースから MOD を取るので、MOD の zip は本体と同じリリースに添付すること。
#>
param(
    [string]$Version = "",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$versionFile = Join-Path $root 'version.json'
$releaseRoot = Join-Path $root 'release'
$appDir = Join-Path $releaseRoot 'JumpDrill'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# ---------------------------------------------------------------- version

if ($Version -ne '') {
    $Version = $Version.TrimStart('v', 'V')
    [System.IO.File]::WriteAllText($versionFile, "{`n  `"version`": `"$Version`"`n}`n", $utf8NoBom)
}
$Version = (Get-Content $versionFile -Raw | ConvertFrom-Json).version
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "version.json のバージョンが x.y.z の形ではありません: $Version"
}
Write-Host "JumpDrill v$Version をビルドします" -ForegroundColor Cyan

# ---------------------------------------------------------------- 使用中チェック

if (Test-Path $appDir) {
    $prefix = [System.IO.Path]::GetFullPath($appDir).TrimEnd('\') + '\'
    $running = @(Get-Process | Where-Object {
        try { $_.MainModule.FileName -and $_.MainModule.FileName.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) } catch { $false }
    })
    if ($running.Count -gt 0) {
        $names = ($running | ForEach-Object { "  $($_.ProcessName) (PID $($_.Id))" }) -join "`n"
        throw "release\JumpDrill の exe が起動中です。閉じてから実行してください。`n$names"
    }
    Remove-Item $appDir -Recurse -Force
}
New-Item $appDir -ItemType Directory -Force | Out-Null

# ---------------------------------------------------------------- GUI

Write-Host "[1/4] GUI を発行しています..." -ForegroundColor Yellow
dotnet publish (Join-Path $root 'src\JumpDrill.Gui\JumpDrill.Gui.csproj') `
    -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:Version=$Version `
    -o $appDir --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "GUI の発行に失敗しました。" }

# ---------------------------------------------------------------- Update.exe

Write-Host "[2/4] Update.exe をビルドしています..." -ForegroundColor Yellow
$updaterOut = Join-Path $root 'src\JumpDrill.Updater\bin\Release\net48'
dotnet build (Join-Path $root 'src\JumpDrill.Updater\JumpDrill.Updater.csproj') `
    -c Release -p:Version=$Version --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Update.exe のビルドに失敗しました。" }
Copy-Item (Join-Path $updaterOut 'Update.exe') $appDir -Force
Copy-Item $versionFile $appDir -Force

# ---------------------------------------------------------------- MOD

Write-Host "[3/4] MOD をビルドしています..." -ForegroundColor Yellow
$modProject = Join-Path $root 'src\JumpDrillMod'
$manifestPath = Join-Path $modProject 'manifest.json'
$manifest = [System.IO.File]::ReadAllText($manifestPath)
$synced = [regex]::Replace($manifest, '("version"\s*:\s*")[^"]*(")', "`${1}$Version`${2}")
if ($synced -ne $manifest) {
    [System.IO.File]::WriteAllText($manifestPath, $synced, $utf8NoBom)
    Write-Host "  manifest.json の version を $Version にしました"
}

# ビルドするゲームの版。deploy_instances.json があればそこに並べた全部、無ければ BeatSaberPath の1つ。
$targets = @()
$instancesPath = Join-Path $root 'deploy_instances.json'
if (Test-Path $instancesPath) {
    # -Encoding UTF8 を明示する。5.1 は BOM 無し UTF-8 を CP932 とみなす
    foreach ($inst in (Get-Content $instancesPath -Raw -Encoding UTF8 | ConvertFrom-Json).instances) {
        $targets += [pscustomobject]@{ Game = $inst.version; Path = (Split-Path -Parent $inst.pluginsPath) }
    }
}
else {
    $targets += [pscustomobject]@{ Game = ($manifest | ConvertFrom-Json).gameVersion; Path = '' }
}

$modBuilds = @()
foreach ($target in $targets) {
    Write-Host "  Beat Saber $($target.Game)"
    $out = Join-Path $modProject "bin\Release\$($target.Game)"
    $buildArgs = @((Join-Path $modProject 'JumpDrillMod.csproj'), '-c', 'Release', "-p:Version=$Version",
                   "-p:GameVersion=$($target.Game)", ('-p:OutputPath="' + $out + '"'), '--nologo', '-v', 'q')
    if ($target.Path -ne '') { $buildArgs += ('-p:BeatSaberPath="' + $target.Path + '"') }
    dotnet build @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "MOD のビルドに失敗しました（Beat Saber $($target.Game)）。パスを確認してください。" }
    $modBuilds += [pscustomobject]@{ Game = $target.Game; Dll = (Join-Path $out 'JumpDrillMod.dll') }
}

# ---------------------------------------------------------------- zip

Write-Host "[4/4] zip を作っています..." -ForegroundColor Yellow
$zip = Join-Path $releaseRoot "JumpDrill-v$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $appDir -DestinationPath $zip

# MOD の zip は、ゲームのフォルダにそのまま展開できる形（Plugins\JumpDrillMod.dll）にする。
# 名前の -bs<ゲームの版> を GUI が見て、入れる先に合うものを選ぶ。名前の形は変えないこと。
Get-ChildItem $releaseRoot -Filter 'JumpDrillMod-v*.zip' | Remove-Item -Force
$modZips = @()
foreach ($build in $modBuilds) {
    $modStage = Join-Path $releaseRoot 'mod-stage'
    if (Test-Path $modStage) { Remove-Item $modStage -Recurse -Force }
    New-Item (Join-Path $modStage 'Plugins') -ItemType Directory -Force | Out-Null
    Copy-Item $build.Dll (Join-Path $modStage 'Plugins') -Force
    $modZip = Join-Path $releaseRoot "JumpDrillMod-v$Version-bs$($build.Game).zip"
    Compress-Archive -Path (Join-Path $modStage 'Plugins') -DestinationPath $modZip
    Remove-Item $modStage -Recurse -Force
    $modZips += $modZip
}

Write-Host ""
foreach ($file in @($zip) + $modZips) {
    Write-Host ("完了: {0} ({1:N1} MB)" -f $file, ((Get-Item $file).Length / 1MB)) -ForegroundColor Green
}
Write-Host ("  MOD は Beat Saber " + (($modBuilds | ForEach-Object { $_.Game }) -join ' / ') + " 用です")
Write-Host ""
Write-Host "GitHub Release の手順:" -ForegroundColor Cyan
Write-Host "  1. git add version.json src/JumpDrillMod/manifest.json; git commit -m `"Release v$Version`""
Write-Host "  2. git tag v$Version; git push origin main v$Version"
Write-Host "  3. GitHub で v$Version の Release を作り、上の zip を全部添付する"
