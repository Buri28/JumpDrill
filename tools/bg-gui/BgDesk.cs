// 見えないデスクトップを作り、その上でプロセスを起動し、前面のウィンドウを記録する。
// Invoke-BgGui.ps1 / Invoke-Hidden.ps1 が Add-Type で読み込む。
// 起動したプロセスは Job Object に入れる。子・孫も同じ Job に入るので、残りの確認と後片付けを PID に頼らずに行える。
using System; using System.Text; using System.Threading; using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class BgDesk {
  [DllImport("user32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
  public static extern IntPtr CreateDesktop(string name, IntPtr dev, IntPtr devmode, int flags, uint access, IntPtr sa);
  [DllImport("user32.dll", SetLastError=true)] public static extern bool CloseDesktop(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumDesktopWindows(IntPtr desk, EnumProc fn, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);

  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  public struct STARTUPINFO { public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
    public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags; public short wShowWindow, cbReserved2;
    public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }
  [StructLayout(LayoutKind.Sequential)] public struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
  [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
  static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
  [DllImport("kernel32.dll")] static extern uint ResumeThread(IntPtr h);
  [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
  [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h, out uint code);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool TerminateProcess(IntPtr h, uint code);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr sa, string name);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr proc);
  [DllImport("kernel32.dll")] static extern bool TerminateJobObject(IntPtr job, uint code);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job, int cls, ref EXTENDED_LIMIT info, int size);
  [DllImport("kernel32.dll")] static extern bool QueryInformationJobObject(IntPtr job, int cls, out ACCOUNTING info, int size, IntPtr ret);
  [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
  [DllImport("kernel32.dll")] static extern bool IsProcessInJob(IntPtr proc, IntPtr job, out bool result);

  [StructLayout(LayoutKind.Sequential)] struct BASIC_LIMIT { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags;
    public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
  [StructLayout(LayoutKind.Sequential)] struct IO_COUNTERS { public ulong a, b, c, d, e, f; }
  [StructLayout(LayoutKind.Sequential)] struct EXTENDED_LIMIT { public BASIC_LIMIT Basic; public IO_COUNTERS Io;
    public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
  [StructLayout(LayoutKind.Sequential)] struct ACCOUNTING { public long TotalUserTime, TotalKernelTime, ThisPeriodTotalUserTime, ThisPeriodTotalKernelTime;
    public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses; }

  /// <summary>起動したプロセス（ハンドルと Job を持つ）。</summary>
  public class Launched { public IntPtr Process, Job; public int Pid; }

  /// <summary>desktop（"WinSta0\名前"）でコマンドを起動し、Job に入れる。既定の flags は CREATE_NO_WINDOW。環境変数は呼んだプロセスのものを継ぐ。</summary>
  public static Launched Start(string desktop, string cmdLine, string cwd) { return Start(desktop, cmdLine, cwd, 0x08000000); }
  public static Launched Start(string desktop, string cmdLine, string cwd, uint creationFlags) {
    IntPtr job = CreateJobObject(IntPtr.Zero, null);
    if (job == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE：この道具が途中で落ちても、Job のハンドルが閉じれば中のプロセスは止まる
    var info = new EXTENDED_LIMIT(); info.Basic.LimitFlags = 0x2000;
    if (!SetInformationJobObject(job, 9, ref info, Marshal.SizeOf(typeof(EXTENDED_LIMIT)))) { int e = Marshal.GetLastWin32Error(); CloseHandle(job); throw new System.ComponentModel.Win32Exception(e); }
    var si = new STARTUPINFO(); si.cb = Marshal.SizeOf(typeof(STARTUPINFO)); si.lpDesktop = desktop;
    PROCESS_INFORMATION pi;
    // CREATE_SUSPENDED で作り、Job に入れてから動かす（子を作る前に Job に入る）
    if (!CreateProcess(null, new StringBuilder(cmdLine), IntPtr.Zero, IntPtr.Zero, false, creationFlags | 0x00000004, IntPtr.Zero, cwd, ref si, out pi)) {
      int e = Marshal.GetLastWin32Error(); CloseHandle(job); throw new System.ComponentModel.Win32Exception(e);
    }
    if (!AssignProcessToJobObject(job, pi.hProcess)) {
      int e = Marshal.GetLastWin32Error(); TerminateProcess(pi.hProcess, 1); CloseHandle(pi.hThread); CloseHandle(pi.hProcess); CloseHandle(job);
      throw new System.ComponentModel.Win32Exception(e);
    }
    ResumeThread(pi.hThread); CloseHandle(pi.hThread);
    return new Launched { Process = pi.hProcess, Job = job, Pid = pi.dwProcessId };
  }
  public static bool Wait(Launched l, int ms) { return WaitForSingleObject(l.Process, (uint)ms) == 0; }
  public static long ExitCode(Launched l) { uint c; return GetExitCodeProcess(l.Process, out c) ? (long)c : -1L; }
  /// <summary>Job の中でまだ動いているプロセスの数（子・孫も含む）。</summary>
  public static int Active(Launched l) { ACCOUNTING a; return QueryInformationJobObject(l.Job, 1, out a, Marshal.SizeOf(typeof(ACCOUNTING)), IntPtr.Zero) ? (int)a.ActiveProcesses : -1; }
  /// <summary>Job の中のプロセスがすべて終わるまで待つ。</summary>
  public static bool WaitAll(Launched l, int ms) {
    var until = DateTime.Now.AddMilliseconds(ms);
    while (Active(l) > 0) { if (DateTime.Now > until) return false; Thread.Sleep(200); }
    return true;
  }
  public static void Terminate(Launched l) { TerminateJobObject(l.Job, 1); }
  public static void Close(Launched l) { if (l == null) return; CloseHandle(l.Process); CloseHandle(l.Job); }
  public static bool InJob(Launched l, uint pid) {
    if (l == null || pid == 0) return false;
    IntPtr h = OpenProcess(0x1000, false, pid); if (h == IntPtr.Zero) return false;   // PROCESS_QUERY_LIMITED_INFORMATION
    bool r; bool ok = IsProcessInJob(h, l.Job, out r); CloseHandle(h); return ok && r;
  }

  /// <summary>デスクトップ上の、Job の中のプロセスの見えている最上位のウィンドウ（持ち主の無いもの）に WM_CLOSE を送る。送った数を返す。</summary>
  public static int CloseWindows(IntPtr desk, Launched l) {
    int n = 0;
    EnumDesktopWindows(desk, (h, p) => { uint pid; GetWindowThreadProcessId(h, out pid);
      if (GetWindow(h, 4) == IntPtr.Zero && IsWindowVisible(h) && InJob(l, pid)) { PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); n++; } return true; }, IntPtr.Zero);
    return n;
  }

  // 前面のウィンドウを ms ごとに記録する。Job を渡すと、そのウィンドウが起動したプロセス（子・孫も）のものかも記録する
  public class Sample { public DateTime At; public long Hwnd; public uint Pid; public bool Launched; }
  static Thread _t; static volatile bool _run; public static List<Sample> Samples = new List<Sample>();
  static volatile Launched _watchJob;
  public static void WatchJob(Launched l) { _watchJob = l; }
  public static void StartWatch(int ms) {
    _run = true; _watchJob = null; lock (Samples) Samples.Clear();
    _t = new Thread(() => { while (_run) { var h = GetForegroundWindow(); uint pid; GetWindowThreadProcessId(h, out pid);
      var s = new Sample { At = DateTime.Now, Hwnd = h.ToInt64(), Pid = pid, Launched = InJob(_watchJob, pid) };
      lock (Samples) Samples.Add(s); Thread.Sleep(ms); } });
    _t.IsBackground = true; _t.Start();
  }
  public static void StopWatch() { _run = false; if (_t != null) _t.Join(); }
}
