<#
    JumpDrill 配布版のビルド

        .\build-release.ps1                  # version.json のバージョンでビルド
        .\build-release.ps1 -Version 0.2.0   # version.json を書き換えてからビルド

    できるもの:
        release\JumpDrill\JumpDrillGui.exe   GUI（.NET ランタイム同梱の単一 exe）
        release\JumpDrill\Update.exe         更新ツール（.NET Framework 4.8）
        release\JumpDrill\version.json
        release\JumpDrill\THIRD-PARTY-NOTICES.txt
                                             exe に入っているサードパーティの通知（licenses\ から）
        release\JumpDrill\LICENSE              JumpDrill 自身のライセンス（リポジトリの LICENSE）
        release\JumpDrill-v<版>.zip          GitHub Release に添付する zip
        release\JumpDrillMod-v<版>-bs<ゲームの版>.zip
                                             MOD（Plugins\JumpDrillMod.dll と、Docs\ の
                                             JumpDrillMod-THIRD-PARTY-NOTICES.txt・JumpDrillMod-LICENSE.txt）。ゲームの版ごとに1つ。
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
$appNotice = Join-Path $root 'licenses\THIRD-PARTY-NOTICES.txt'
$modNotice = Join-Path $root 'licenses\JumpDrillMod-THIRD-PARTY-NOTICES.txt'
# JumpDrill 自身のライセンス。アプリの zip には LICENSE、MOD の zip には JumpDrillMod-LICENSE.txt として入れる
$licenseFile = Join-Path $root 'LICENSE'

# 配布物に入るサードパーティ（名前と版）が、通知ファイルの一覧に全部あるか確かめる。
# 版を上げたりライブラリを足したりしたら、licenses\ の通知を直すまでビルドを止める。
function Assert-Notice([string]$noticePath, [string[]]$items) {
    $text = [System.IO.File]::ReadAllText($noticePath)
    $missing = @($items | Where-Object { $text -notmatch ('(?m)\b' + [regex]::Escape($_) + '(?![\w.])') })
    if ($missing.Count -gt 0) {
        throw "$(Split-Path -Leaf $noticePath) に次のものがありません。licenses\ の通知を直してください:`n  " + ($missing -join "`n  ")
    }
}

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
# 通知の照合に使う publish の deps.json。古いものを読まないよう、先に消しておく
$guiProject = Join-Path $root 'src\JumpDrill.Gui\JumpDrill.Gui.csproj'
$guiTfm = ([regex]::Match([System.IO.File]::ReadAllText($guiProject), '<TargetFramework>([^<]+)</TargetFramework>')).Groups[1].Value
$guiDepsPath = Join-Path $root "src\JumpDrill.Gui\obj\Release\$guiTfm\$Runtime\JumpDrillGui.deps.json"
if (Test-Path $guiDepsPath) { Remove-Item $guiDepsPath -Force }
dotnet publish $guiProject `
    -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:Version=$Version `
    -o $appDir --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "GUI の発行に失敗しました。" }

# exe に入ったもの: 中身のあるパッケージと .NET ランタイム（publish の deps.json から読む）
if (-not (Test-Path $guiDepsPath)) { throw "publish の deps.json がありません: $guiDepsPath" }
$guiDeps = Get-Content $guiDepsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$guiTarget = $guiDeps.targets.PSObject.Properties | Where-Object { $_.Name -like "*/$Runtime" } | Select-Object -First 1
$guiItems = @()
foreach ($lib in $guiDeps.libraries.PSObject.Properties) {
    $name, $ver = $lib.Name -split '/', 2
    if ($lib.Value.type -eq 'runtimepack') {
        $guiItems += (($name -replace '^runtimepack\.', '') -replace "\.Runtime\.$([regex]::Escape($Runtime))$", '') + " $ver"
    }
    elseif ($lib.Value.type -eq 'package') {
        $assets = $guiTarget.Value.($lib.Name)
        if ($assets -and ($assets.runtime -or $assets.native)) { $guiItems += "$name $ver" }
    }
}
Assert-Notice $appNotice $guiItems
Copy-Item $appNotice (Join-Path $appDir 'THIRD-PARTY-NOTICES.txt') -Force
Copy-Item $licenseFile (Join-Path $appDir 'LICENSE') -Force

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

# DLL に ILRepack で入るもの: 実行時の DLL があるパッケージ（project.assets.json から読む）
$modAssets = Get-Content (Join-Path $modProject 'obj\project.assets.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$modItems = @()
foreach ($target in $modAssets.targets.PSObject.Properties) {
    foreach ($lib in $target.Value.PSObject.Properties) {
        if ($lib.Value.type -ne 'package' -or -not $lib.Value.runtime) { continue }
        $files = @($lib.Value.runtime.PSObject.Properties.Name | Where-Object { $_ -notlike '*/_._' })
        if ($files.Count -gt 0) { $modItems += ($lib.Name -replace '/', ' ') }
    }
}
Assert-Notice $modNotice ($modItems | Sort-Object -Unique)

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
    # 通知と LICENSE は配布物に入っていればよいので、zip の Docs に入れる（UserData はゲームの設定の置き場なので使わない）。
    # Docs は他の MOD と同じフォルダになりうるので、名前に JumpDrillMod- を付ける
    New-Item (Join-Path $modStage 'Docs') -ItemType Directory -Force | Out-Null
    Copy-Item $modNotice (Join-Path $modStage 'Docs') -Force
    Copy-Item $licenseFile (Join-Path $modStage 'Docs\JumpDrillMod-LICENSE.txt') -Force
    $modZip = Join-Path $releaseRoot "JumpDrillMod-v$Version-bs$($build.Game).zip"
    Compress-Archive -Path (Join-Path $modStage 'Plugins'), (Join-Path $modStage 'Docs') -DestinationPath $modZip
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
