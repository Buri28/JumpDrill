<#
deploy-mod.ps1
deploy_instances.json に並べたインスタンスごとに JumpDrillMod をビルドして配る。

  powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy-mod.ps1

配るのは Plugins\JumpDrillMod.dll の1本だけ。JumpDrill.Core と OggVorbisEncoder、
その BCL の補助は、ビルドの後で ILRepack が JumpDrillMod.dll へ取り込んでいる（ILRepack.targets）。

古い版は Core と OggVorbisEncoder を Libs\ に別に置いていた。取り込んだ今は要らないので、
残っていれば消す（古い Core が残っていても読まれはしないが、どれが使われているか紛らわしい）。
BCL の補助（System.Memory など）は他の MOD が使っているかもしれないので触らない。
#>

Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
$configPath = Join-Path $scriptRoot 'deploy_instances.json'
if (-not (Test-Path $configPath)) {
    Write-Error "deploy_instances.json が見つかりません。deploy_instances.sample.json を複製して作ってください。"
    exit 1
}

# -Encoding UTF8 を明示する。Windows PowerShell 5.1 は BOM 無し UTF-8 を CP932 と
# みなすので、日本語が混ざっていると 0x5C（\）が現れて JSON が壊れる
$json = Get-Content $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
$projPath = Join-Path $scriptRoot 'src\JumpDrillMod\JumpDrillMod.csproj'
if (-not (Test-Path $projPath)) {
    Write-Error "Project file not found: $projPath"
    exit 1
}

# 取り込み済みになったので、Libs から片付ける DLL。どちらもこの MOD が置いたもの
$obsoleteLibs = @('JumpDrill.Core.dll', 'OggVorbisEncoder.dll')

foreach ($inst in $json.instances) {
    $ver = $inst.version
    $pluginsPath = $inst.pluginsPath
    # csproj が欲しいのは Plugins ではなくインスタンスのルート
    $bsPath = Split-Path -Parent $pluginsPath

    Write-Host "=== Building JumpDrillMod for $ver (BeatSaberPath=$bsPath) ==="

    $outFull = Join-Path $scriptRoot "src\JumpDrillMod\bin\Debug\$ver"
    New-Item -ItemType Directory -Path $outFull -Force | Out-Null

    # /p の値は引用符で囲む。囲まないと MSBuild が空白で割ってしまう
    $buildArgs = @(
        $projPath,
        '-c', 'Debug',
        ('/p:BeatSaberPath="' + $bsPath + '"'),
        ('/p:GameVersion=' + $ver),
        ('/p:OutputPath="' + $outFull + '"')
    )

    & dotnet build @buildArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed for $ver"
        exit $LASTEXITCODE
    }

    if ([string]::IsNullOrWhiteSpace($pluginsPath)) {
        Write-Warning "No pluginsPath configured for $ver; skipping deploy"
        continue
    }

    try {
        $builtDll = Join-Path $outFull 'JumpDrillMod.dll'
        if (-not (Test-Path $builtDll)) {
            Write-Warning "Built DLL not found: $builtDll"
            continue
        }
        Copy-Item -Path $builtDll -Destination (Join-Path $pluginsPath 'JumpDrillMod.dll') -Force
        Write-Host "Deployed: $pluginsPath\JumpDrillMod.dll"

        $libsPath = Join-Path $bsPath 'Libs'
        foreach ($name in $obsoleteLibs) {
            $old = Join-Path $libsPath $name
            if (Test-Path $old) {
                Remove-Item -Path $old -Force
                Write-Host "Removed (now merged): $old"
            }
        }
    }
    catch [System.IO.IOException] {
        # 配布先の DLL はゲームが掴んでいる。ビルドは通っているので、
        # ゲームを閉じてもう一度流せばよい
        Write-Warning "Could not replace the DLL in $pluginsPath. Close Beat Saber and run this again."
        Write-Warning $_.Exception.Message
    }
    catch {
        Write-Warning "Deployment to $pluginsPath failed: $_"
    }

    Write-Host ""
}

Write-Host "All builds completed." -ForegroundColor Green
