<#
    JumpDrill 起動スクリプト（PowerShell 用）

        .\drill.ps1 --seq "R8b, La1" --interval 110 --sec 20
        .\drill.ps1 --seq "R8b" --mirror --hands split --install
        .\drill.ps1 --rebuild --help

    初回だけ Release ビルドを走らせ、以後はビルド済みの exe を直接叩く。
    ソースを触ったあとは --rebuild を付ける。
    カレントディレクトリは変えないので --out は呼び出し元からの相対で効く。

    cmd から使うときは drill.bat の方を叩く。
    PowerShell からは必ずこちら（drill.ps1）を使うこと。
    drill.bat を PowerShell から呼ぶと、PowerShell が引用符を落としたうえで
    cmd.exe が "R:8>b" の > をリダイレクトとして解釈してしまい、
    遷移が黙って "R:8" に化ける。
#>

# param() を置かないのは、--seq のような二重ダッシュの引数を
# PowerShell のパラメータ束縛に触らせず $args にそのまま流し込むため。
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root 'src\JumpDrill.Cli\bin\Release\net8.0\drill.exe'
# ソリューション全体ではなく CLI だけ建てる。
# GUI が起動したままだと JumpDrill.Core.dll がロックされ、
# 全体ビルドは巻き添えで失敗する。
$proj = Join-Path $root 'src\JumpDrill.Cli\JumpDrill.Cli.csproj'

$rebuild = $false
$rest = @()
foreach ($a in $args) {
    if ($a -is [string] -and $a -ieq '--rebuild') { $rebuild = $true }
    else { $rest += $a }
}

# ソースが exe より新しければ黙って建て直す。
# 忘れると古いビルドが動いて「直したはずの挙動にならない」ハマり方をする。
$stale = $false
if (Test-Path $exe) {
    $exeTime = (Get-Item $exe).LastWriteTimeUtc
    $newest = Get-ChildItem (Join-Path $root 'src') -Recurse -File -Include *.cs, *.csproj |
        Where-Object { $_.FullName -notlike '*\bin\*' -and $_.FullName -notlike '*\obj\*' } |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($newest -and $newest.LastWriteTimeUtc -gt $exeTime) { $stale = $true }
}

if ($rebuild -or $stale -or -not (Test-Path $exe)) {
    Write-Host '[drill] building...'
    dotnet build $proj -c Release -v q --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host '[drill] ビルドに失敗しました。' -ForegroundColor Red
        exit 1
    }
}

if (-not (Test-Path $exe)) {
    Write-Host "[drill] 実行ファイルが見つかりません: $exe" -ForegroundColor Red
    exit 1
}

& $exe @rest
exit $LASTEXITCODE
