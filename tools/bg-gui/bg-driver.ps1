# 見えないデスクトップの中で動く操作役。Invoke-BgGui.ps1 から起動される（直接は使わない）。
# マウス・キーの入力や SetForegroundWindow は使わない。ウィンドウへのメッセージと UI Automation の Pattern だけで操作する。
#
# 手順（steps.txt に1行1つ）:
#   launch              exe を起動して -MainTitle（既定 "JumpDrill*"）のウィンドウを待つ
#   win=<題名>           GUI の最上位のウィンドウを題名（ワイルドカード）で選ぶ（最大 20 秒待つ）
#   shot=<名前>          今のウィンドウを PrintWindow で <名前>.png に撮る（2回続けて同じ絵になるまで待つ）
#   shotflag=<数>        撮り方を変える。-1（既定）＝ウィンドウの枠は 2・中身は WM_PRINT で重ねる。0 / 2 = PrintWindow の flag そのまま
#   click=<名前>         今のウィンドウのボタンを押す（PostMessage BM_CLICK。モーダルのウィンドウが開いても止まらない）。
#                       フォルダ・URL・ArcViewer を開く／MOD・更新のボタンは断る（見える画面にウィンドウが出る・ユーザーのファイルに触れる）。
#                       どうしても押すときは clickany=<名前>
#   invoke=<名前>        UI Automation の InvokePattern で押す
#   combo=<番号>:<文字>  今のウィンドウの <番号> 番目（0 から、combos の並び）のプルダウンで <文字> から始まる項目を選ぶ
#   value=<番号>:<値>    今のウィンドウの <番号> 番目の入力欄（Edit）に ValuePattern で値を入れる
#   drop=<番号>:<名前>   <番号> 番目のプルダウンの項目をログに出し、開いた一覧をウィンドウに重ねて <名前>.png に撮って閉じる
#   check=<名前>:on|off  チェックボックスをその状態にする
#   cell=<行>:<列>       メダル画面の表のマスを選ぶ（0 から）。ウィンドウの既定の大きさ・スクロールしていない表のとき用
#                       （表の中央寄せ・スクロール位置は見ない）。選んだマスは生成オプションのウィンドウの「対象」などで確かめる
#   nowin=<題名>         そのウィンドウが開いていないかをログに出す
#   combos / buttons / edits / texts / windows   一覧をログに出す
#   wait=<ミリ秒>
#   waitnotext=<文字>    今のウィンドウからその表示（ワイルドカード）が消えるまで待つ（読み込み中の表示など）
#   close               今のウィンドウに WM_CLOSE を送る（メインのウィンドウならプロセスの終了を待つ）
param([string]$Exe, [string]$StepsFile, [string]$OutDir, [string]$PidFile, [string]$MainTitle = "JumpDrill*")
$ErrorActionPreference = "Stop"
$log = Join-Path $OutDir "driver-log.txt"
function L($t) { ("  [drv {0}] {1}" -f (Get-Date -Format "HH:mm:ss.fff"), $t) | Out-File -Append -Encoding utf8 $log }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class U {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern IntPtr GetThreadDesktop(uint tid);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool RedrawWindow(IntPtr h, IntPtr rc, IntPtr rgn, uint flags);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool GetUserObjectInformation(IntPtr h, int i, StringBuilder b, int n, out int need);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct COMBOBOXINFO { public int cbSize; public RECT rcItem, rcButton; public int stateButton; public IntPtr hwndCombo, hwndItem, hwndList; }
  [DllImport("user32.dll")] public static extern bool GetComboBoxInfo(IntPtr h, ref COMBOBOXINFO i);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, StringBuilder l);
  public static string DesktopName() { var b = new StringBuilder(256); int n; GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, b, 512, out n); return b.ToString(); }
}
"@
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
L "driver on desktop: $([U]::DesktopName())"

$script:proc = $null
# 撮り方。既定（-1）は、ウィンドウの枠を 2（PW_RENDERFULLCONTENT）で撮り、中身を 1（PW_CLIENTONLY、WM_PRINT で描かせる）で撮って重ねる。
# 2 だけだと見えないデスクトップで和文と英数字が混ざる行の英数字が欠けることがあり、0 だけだとタイトルバーが旧来の形で描かれるため
$script:flags = @(-1)
# 押すと見える画面にウィンドウを出す・ユーザーのファイルに触れるボタン
$script:deny = '開く|ArcViewer|プレビュー|MOD|更新|最新版|Open|Preview|Update|\.\.\.'
$script:win = $null

function TopWindows() {
  $c = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $script:proc.Id)
  return @($AE::RootElement.FindAll($TS::Children, $c))
}
function FindWin($pattern) {
  for ($i = 0; $i -lt 40; $i++) {
    foreach ($w in (TopWindows)) { if ($w.Current.Name -like $pattern) { return $w } }
    # モーダルのウィンドウは持ち主の子として見えることもある
    foreach ($w in (TopWindows)) {
      $c = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Window)
      foreach ($cw in $w.FindAll($TS::Descendants, $c)) { if ($cw.Current.Name -like $pattern) { return $cw } }
    }
    Start-Sleep -Milliseconds 500
  }
  throw "window not found: $pattern"
}
function All($root, $type) {
  $c = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $type)
  return @($root.FindAll($TS::Descendants, $c))
}
function ButtonByName($name) {
  foreach ($b in (All $script:win $CT::Button)) { if ($b.Current.Name -eq $name) { return $b } }
  throw "button not found: $name"
}
function IsBlank($bmp) {
  $first = $bmp.GetPixel([int]($bmp.Width / 2), [int]($bmp.Height / 2)).ToArgb()
  for ($y = 0; $y -lt $bmp.Height; $y += 13) { for ($x = 0; $x -lt $bmp.Width; $x += 17) { if ($bmp.GetPixel($x, $y).ToArgb() -ne $first) { return $false } } }
  return $true
}
function Raw($h, $w, $hh, $flag) {
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
  $ok = [U]::PrintWindow($h, $dc, $flag); $g.ReleaseHdc($dc); $g.Dispose()
  if ($ok -and -not (IsBlank $bmp)) { return $bmp }
  L "PrintWindow flag=$flag ok=$ok blank"
  $bmp.Dispose(); return $null
}
function Capture($h, $w, $hh) {
  foreach ($flag in $script:flags) {
    if ($flag -ge 0) { $bmp = Raw $h $w $hh $flag; if ($bmp) { return @($bmp, $flag) }; continue }
    $frame = Raw $h $w $hh 2
    if (-not $frame) { $frame = Raw $h $w $hh 0 }
    if (-not $frame) { continue }
    $cr = New-Object U+RECT; [U]::GetClientRect($h, [ref]$cr) | Out-Null
    $wr = New-Object U+RECT; [U]::GetWindowRect($h, [ref]$wr) | Out-Null
    $pt = New-Object U+POINT; [U]::ClientToScreen($h, [ref]$pt) | Out-Null
    if ($cr.R -gt 0 -and $cr.B -gt 0) {
      $client = Raw $h $cr.R $cr.B 1
      if ($client) {
        $g = [System.Drawing.Graphics]::FromImage($frame)
        $g.DrawImageUnscaled($client, $pt.X - $wr.L, $pt.Y - $wr.T); $g.Dispose(); $client.Dispose()
        return @($frame, -1)
      }
    }
    return @($frame, 2)
  }
  return @($null, -2)
}
function PngBytes($bmp) { $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); return ,$ms.ToArray() }
function Shot($name) {
  # 描き終わるまで待つ：0.6 秒おきに撮り、2回続けて同じ絵になったものを保存する（最大 10 秒）
  $h = [IntPtr]$script:win.Current.NativeWindowHandle
  # 子のコントロールまで描き直させる（RDW_INVALIDATE|ERASE|FRAME|ALLCHILDREN|UPDATENOW）。見えないデスクトップでは描きかけのまま残ることがあった
  [U]::RedrawWindow($h, [IntPtr]::Zero, [IntPtr]::Zero, 0x0001 -bor 0x0004 -bor 0x0400 -bor 0x0080 -bor 0x0100) | Out-Null
  $prev = $null; $prevBytes = $null; $stable = $false
  for ($i = 0; $i -lt 17; $i++) {
    $r = New-Object U+RECT; [U]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp, $flag = Capture $h ($r.R - $r.L) ($r.B - $r.T)
    if ($bmp -eq $null) { if ($prev) { $prev.Dispose() }; throw "shot failed: $name" }
    $bytes = PngBytes $bmp
    if ($prevBytes -ne $null -and [Convert]::ToBase64String($bytes) -eq [Convert]::ToBase64String($prevBytes)) { $stable = $true; $bmp.Dispose(); break }
    if ($prev) { $prev.Dispose() }
    $prev = $bmp; $prevBytes = $bytes
    Start-Sleep -Milliseconds 600
  }
  $path = Join-Path $OutDir "$name.png"; $prev.Save($path); $prev.Dispose()
  L ("shot {0}  size={1}x{2} rect={3},{4} flag={5} stable={6} tries={7}" -f $path, ($r.R - $r.L), ($r.B - $r.T), $r.L, $r.T, $flag, $stable, ($i + 1))
}
function SelectCombo($index, $prefix) {
  $cb = (All $script:win $CT::ComboBox)[$index]
  $h = [IntPtr]$cb.Current.NativeWindowHandle
  $i = [U]::SendMessage($h, 0x014C, [IntPtr](-1), $prefix).ToInt32()   # CB_FINDSTRING
  if ($i -lt 0) { throw "combo $index has no item starting with '$prefix'" }
  [U]::SendMessage($h, 0x014E, [IntPtr]$i, [IntPtr]::Zero) | Out-Null  # CB_SETCURSEL
  # CB_SETCURSEL は CBN_SELCHANGE を出さないので、WinForms が受ける反射の WM_COMMAND（0x2111）で知らせる
  [U]::SendMessage($h, 0x2111, [IntPtr](1 -shl 16), $h) | Out-Null
  Start-Sleep -Milliseconds 500
  L "combo[$index] -> item $i  value now: $(ComboValue $cb)"
}
function DropShot($index, $name) {
  # 開いた一覧は別のウィンドウなので、ウィンドウと一覧を別々に撮って一覧の位置に重ねる
  $cbs = All $script:win $CT::ComboBox
  if ($index -lt 0 -or $index -ge $cbs.Count) { throw "drop: no combo #$index (combos=$($cbs.Count))" }
  $cb = $cbs[$index]
  $h = [IntPtr]$cb.Current.NativeWindowHandle
  $count = [U]::SendMessage($h, 0x0146, [IntPtr]::Zero, [IntPtr]::Zero).ToInt32()   # CB_GETCOUNT
  $items = for ($i = 0; $i -lt $count; $i++) { $sb = New-Object System.Text.StringBuilder 256; [U]::SendMessage($h, 0x0148, [IntPtr]$i, $sb) | Out-Null; $sb.ToString() }  # CB_GETLBTEXT
  L ("combo[{0}] items={1}: {2}  selected='{3}'" -f $index, $count, ($items -join ' / '), (ComboValue $cb))
  [U]::SendMessage($h, 0x014F, [IntPtr]1, [IntPtr]::Zero) | Out-Null   # CB_SHOWDROPDOWN
  Start-Sleep -Milliseconds 800
  try {
    $info = New-Object U+COMBOBOXINFO; $info.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($info)
    [U]::GetComboBoxInfo($h, [ref]$info) | Out-Null
    $hw = [IntPtr]$script:win.Current.NativeWindowHandle
    $wr = New-Object U+RECT; [U]::GetWindowRect($hw, [ref]$wr) | Out-Null
    $lr = New-Object U+RECT; [U]::GetWindowRect($info.hwndList, [ref]$lr) | Out-Null
    if ($info.hwndList -eq [IntPtr]::Zero) { throw "drop: list window not found for combo #$index" }
    $main, $null = Capture $hw ($wr.R - $wr.L) ($wr.B - $wr.T)
    $list = Raw $info.hwndList ($lr.R - $lr.L) ($lr.B - $lr.T) 2
    if (-not $list) { $list = Raw $info.hwndList ($lr.R - $lr.L) ($lr.B - $lr.T) 0 }
    if (-not $main -or -not $list) { throw "drop shot failed: $name" }
    # 一覧がウィンドウの上や左にはみ出しても切れないよう、両方を囲む範囲で描く
    $L0 = [Math]::Min($wr.L, $lr.L); $T0 = [Math]::Min($wr.T, $lr.T)
    $W = [Math]::Max($wr.R, $lr.R) - $L0; $H = [Math]::Max($wr.B, $lr.B) - $T0
    $out = New-Object System.Drawing.Bitmap $W, $H
    $g = [System.Drawing.Graphics]::FromImage($out); $g.Clear([System.Drawing.Color]::Black)
    $g.DrawImageUnscaled($main, $wr.L - $L0, $wr.T - $T0); $g.DrawImageUnscaled($list, $lr.L - $L0, $lr.T - $T0); $g.Dispose()
    $path = Join-Path $OutDir "$name.png"; $out.Save($path); $out.Dispose(); $main.Dispose(); $list.Dispose()
    L ("shot {0}  list rect={1},{2},{3},{4}" -f $path, $lr.L, $lr.T, $lr.R, $lr.B)
  } finally {
    [U]::SendMessage($h, 0x014F, [IntPtr]0, [IntPtr]::Zero) | Out-Null
    Start-Sleep -Milliseconds 300
  }
}
function ComboValue($cb) { try { $cb.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { "?" } }

$code = 0
try {
  foreach ($raw in (Get-Content -Encoding utf8 $StepsFile)) {
    $s = $raw.Trim(); if (-not $s) { continue }
    $k, $v = $s.Split('=', 2)
    L "step: $s"
    switch ($k) {
      "launch" {
        $psi = New-Object System.Diagnostics.ProcessStartInfo $Exe
        $psi.UseShellExecute = $false   # 親（このデスクトップ）を継がせる
        $psi.WorkingDirectory = Split-Path $Exe
        $script:proc = [System.Diagnostics.Process]::Start($psi)
        Set-Content -Path $PidFile -Value $script:proc.Id
        L ("launched pid={0} at {1}" -f $script:proc.Id, $script:proc.StartTime.ToString("yyyy-MM-dd HH:mm:ss.fff"))
        $script:win = FindWin $MainTitle; Start-Sleep -Milliseconds 1500
      }
      "win" { $script:win = FindWin $v; Start-Sleep -Milliseconds 800; L "window: $($script:win.Current.Name)" }
      "shot" { Shot $v }
      "clickany" { $b = ButtonByName $v; [U]::PostMessage([IntPtr]$b.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; Start-Sleep -Milliseconds 800 }
      "click" { if ($v -match $script:deny) { throw "refused to press '$v' (opens something outside or touches files). Use clickany= if you really need it." }; $b = ButtonByName $v; [U]::PostMessage([IntPtr]$b.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; Start-Sleep -Milliseconds 800 }
      "invoke" { if ($v -match $script:deny) { throw "refused to press '$v'. Use clickany= if you really need it." }; (ButtonByName $v).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 800 }
      "combo" { $n, $t = $v.Split(':', 2); SelectCombo ([int]$n) $t }
      "drop" { $n, $t = $v.Split(':', 2); DropShot ([int]$n) $t }
      "value" {
        $n, $t = $v.Split(':', 2)
        (All $script:win $CT::Edit)[[int]$n].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($t); Start-Sleep -Milliseconds 300
      }
      "combos" { $i = 0; foreach ($cb in (All $script:win $CT::ComboBox)) { L ("  combo[{0}] value='{1}' rect={2}" -f $i, (ComboValue $cb), $cb.Current.BoundingRectangle); $i++ } }
      "buttons" { foreach ($b in (All $script:win $CT::Button)) { L ("  button '{0}' enabled={1}" -f $b.Current.Name, $b.Current.IsEnabled) } }
      "edits" { $i = 0; foreach ($e in (All $script:win $CT::Edit)) { $t = ""; try { $t = $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch {}; L ("  edit[{0}] '{1}'" -f $i, ($t -replace "`r?`n", " / ")); $i++ } }
      "windows" { foreach ($w in (TopWindows)) { L ("  window '{0}' h={1}" -f $w.Current.Name, $w.Current.NativeWindowHandle) } }
      "check" {
        # check=<名前>:on|off  チェックボックスをその状態にする（違うときだけ BM_CLICK。WinForms の CheckedChanged も走る）
        $n, $t = $v.Split(':', 2)
        $cb = $null
        foreach ($c in (All $script:win $CT::CheckBox)) { if ($c.Current.Name -eq $n) { $cb = $c; break } }
        if ($cb -eq $null) { throw "checkbox not found: $n" }
        $want = $t -eq "on"
        $tp = $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        $now = $tp.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On
        if ($now -ne $want) { [U]::PostMessage([IntPtr]$cb.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; Start-Sleep -Milliseconds 500 }
        L ("  checkbox '{0}' now={1}" -f $n, $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState)
      }
      "cell" {
        # cell=<行>:<列>  メダル画面の表のマスを左クリック（WM_LBUTTONDOWN/UP をウィンドウへ送る。座標は MedalBoard の 128/62/18/48 × DPI/96）
        $r, $c = $v.Split(':', 2)
        $best = $null
        foreach ($pane in (All $script:win $CT::Pane)) {
          $rc = $pane.Current.BoundingRectangle
          if ($pane.Current.NativeWindowHandle -ne 0 -and $rc.Height -gt 300 -and ($best -eq $null -or $rc.Top -gt $best.Current.BoundingRectangle.Top)) { $best = $pane }
        }
        if ($best -eq $null) { throw "medal board not found" }
        $h = [IntPtr]$best.Current.NativeWindowHandle
        $s = [U]::GetDpiForWindow($h) / 96.0
        $x = [int](128 * $s + ([int]$c + 0.5) * 62 * $s); $y = [int](18 * $s + ([int]$r + 0.5) * 48 * $s)
        $lp = [IntPtr](($y -shl 16) -bor $x)
        [U]::PostMessage($h, 0x0201, [IntPtr]1, $lp) | Out-Null; Start-Sleep -Milliseconds 100
        [U]::PostMessage($h, 0x0202, [IntPtr]0, $lp) | Out-Null; Start-Sleep -Milliseconds 500
        L "  clicked cell ($r,$c) at $x,$y"
      }
      "texts" { foreach ($t in (All $script:win $CT::Text)) { if ($t.Current.Name) { L ("  text '{0}'" -f ($t.Current.Name -replace "`r?`n", " / ")) } } }
      "nowin" {
        # nowin=<題名>  そのウィンドウが開いていないこと（1.5 秒待ってから、最上位と子のウィンドウを探す）をログに出す
        Start-Sleep -Milliseconds 1500
        $found = $false
        foreach ($w in (TopWindows)) {
          if ($w.Current.Name -like $v) { $found = $true }
          $cnd = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Window)
          foreach ($cw in $w.FindAll($TS::Descendants, $cnd)) { if ($cw.Current.Name -like $v) { $found = $true } }
        }
        L "  nowin '$v': open=$found"
      }
      "shotflag" { $script:flags = @([int]$v) }
      "wait" { Start-Sleep -Milliseconds ([int]$v) }
      "waitnotext" {
        # 今のウィンドウにこの文字（ワイルドカード）の表示が無くなるまで待つ（最大 60 秒）
        for ($i = 0; $i -lt 120; $i++) {
          $hit = @(All $script:win $CT::Text | Where-Object { $_.Current.Name -like $v })
          if ($hit.Count -eq 0) { break }
          Start-Sleep -Milliseconds 500
        }
        L "waitnotext '$v': gone=$($hit.Count -eq 0) after $([int]($i / 2)) s"
      }
      "close" {
        $h = [IntPtr]$script:win.Current.NativeWindowHandle
        [U]::PostMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Start-Sleep -Milliseconds 1000
        if ((TopWindows).Count -eq 0 -or $script:proc.HasExited) {
          if ($script:proc.WaitForExit(10000)) { L "process exited (code $($script:proc.ExitCode))" }
        }
      }
      default { throw "unknown step: $s" }
    }
  }
} catch {
  L "ERROR: $($_.Exception.Message)"
  $code = 1
}
# 自分が起動した GUI がまだ残っていれば、まず普通に閉じる
if ($script:proc -and -not $script:proc.HasExited) {
  # 子のウィンドウ（メダル・ダイアログ）が開いたままだとメインのウィンドウが閉じないので、全部のウィンドウに WM_CLOSE を送る
  L "closing GUI windows with WM_CLOSE"
  for ($try = 0; $try -lt 5 -and -not $script:proc.HasExited; $try++) {
    try { foreach ($w in (TopWindows)) { [U]::PostMessage([IntPtr]$w.Current.NativeWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null } } catch {}
    $script:proc.WaitForExit(2000) | Out-Null
  }
  if ($script:proc.HasExited) { L "GUI exited" } else { L "GUI did not exit; killing"; $script:proc.Kill() }
}
exit $code
