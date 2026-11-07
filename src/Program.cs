// disk-cleaner v1.1.0
// Windows C: drive cleanup tool.
// SAFE BY DEFAULT: without --apply it only MEASURES and prints a report (dry-run).
// Sizes reported are REAL ON-DISK ALLOCATION (GetCompressedFileSize), not logical length,
// so sparse/preallocated caches (e.g. video-editor caches) are not overstated.
//
// What it never touches: user documents, Downloads, Desktop, phone/cross-device mounts,
// Windows\System32, WinSxS files, Windows\Installer (MSI cache), Program Files, drivers,
// chat databases (QQ/WeChat), video-editor drafts.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace DiskCleaner;

internal static class Program
{
    private const string Version = "1.1.0";

    private sealed record Target(string Label, string Path, bool Admin);

    private static readonly List<(string Label, long Bytes, string Path, bool Admin)> Report = new();
    private static long _freedTotal;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var apply = args.Contains("--apply");
        var yes = args.Contains("--yes");
        var gameCaches = args.Contains("--game-caches");
        var dism = args.Contains("--dism");
        var noElevate = args.Contains("--no-elevate");
        if (args.Contains("--help") || args.Contains("-h")) { PrintHelp(); return 0; }

        // ---- self-elevate when actually deleting ----
        if (apply && !noElevate && !IsAdmin())
        {
            try
            {
                var own = Environment.ProcessPath ?? throw new InvalidOperationException("cannot resolve exe path");
                var psi = new ProcessStartInfo(own)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = string.Join(' ', args.Select(Quote))
                };
                Console.WriteLine("  需要管理员权限，正在请求 UAC 提权...");
                var child = Process.Start(psi) ?? throw new InvalidOperationException("failed to start elevated process");
                child.WaitForExit();
                return child.ExitCode;
            }
            catch (Win32Exception)
            {
                Console.WriteLine("  提权被取消，已退出（未做任何修改）。");
                return 3;
            }
            catch (Exception ex)
            {
                Console.WriteLine("  提权失败: " + ex.Message);
                return 3;
            }
        }

        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        // ---------------- target list ----------------
        var targets = new List<Target>();

        void U(string label, string path) => targets.Add(new Target(label, path, false));
        void S(string label, string path) => targets.Add(new Target(label, path, true));

        // --- user level: temp / shader / crash ---
        U("用户临时文件 (User Temp)", Path.Combine(local, "Temp"));
        U("Edge 浏览缓存", Path.Combine(local, @"Microsoft\Edge\User Data\Default\Cache"));
        U("Edge 代码缓存", Path.Combine(local, @"Microsoft\Edge\User Data\Default\Code Cache"));
        U("Edge GPU 缓存", Path.Combine(local, @"Microsoft\Edge\User Data\Default\GPUCache"));
        U("Edge 站点缓存", Path.Combine(local, @"Microsoft\Edge\User Data\Default\Service Worker\CacheStorage"));
        U("npm 缓存", Path.Combine(local, "npm-cache"));
        U("pnpm 存储", Path.Combine(local, @"pnpm\store"));
        U("pip 缓存", Path.Combine(local, @"pip\Cache"));
        U("DX 着色器缓存", Path.Combine(local, "D3DSCache"));
        U("崩溃转储 (CrashDumps)", Path.Combine(local, "CrashDumps"));
        U("Windows 错误上报 (用户)", Path.Combine(local, @"Microsoft\Windows\WER"));
        U("Office 文件缓存", Path.Combine(local, @"Office\16.0\OfficeFileCache"));
        U("远程桌面缓存 (RDP)", Path.Combine(local, "Terminal Server Client"));
        U("缩略图缓存 (Explorer)", Path.Combine(local, @"Microsoft\Windows\Explorer"));

        // --- user level: NVIDIA driver package cache ---
        U("NVIDIA 驱动安装包缓存", Path.Combine(programData, @"NVIDIA Corporation\NVIDIA app\UpdateFramework\ota-artifacts"));

        // --- user level: video editor (Jianying / CapCut) ---
        U("剪映 素材缓存", Path.Combine(local, @"JianyingPro\User Data\Cache"));
        U("剪映 日志", Path.Combine(local, @"JianyingPro\User Data\Log"));
        U("剪映 CEF 缓存", Path.Combine(local, @"JianyingPro\User Data\CEF\Cache"));
        U("剪映 CEF 着色器缓存", Path.Combine(local, @"JianyingPro\User Data\CEF\ShaderCache"));

        // --- user level: Tencent ---
        U("企业微信 升级包", Path.Combine(roaming, @"Tencent\WXWork\upgrade"));
        U("企业微信 日志", Path.Combine(roaming, @"Tencent\WXWork\Log"));
        U("微信4.x 日志", Path.Combine(roaming, @"Tencent\xwechat\log"));
        U("微信4.x 崩溃转储", Path.Combine(roaming, @"Tencent\xwechat\crashinfo"));

        // --- user level: streaming / music / chat apps ---
        U("直播伴侣 礼物素材缓存", Path.Combine(roaming, @"webcast_mate\gift"));
        U("直播伴侣 SDK 日志", Path.Combine(roaming, @"webcast_mate\mediasdk_log"));
        U("直播伴侣 日志", Path.Combine(roaming, @"webcast_mate\logs"));
        U("酷狗 封面图缓存", Path.Combine(roaming, @"KuGou8\ImagesCache"));
        U("酷狗 旧升级包", Path.Combine(roaming, @"KuGou8\Upgrade"));
        U("酷狗 CEF 缓存", Path.Combine(roaming, @"KuGou8\CefCache89"));
        U("酷狗 日志", Path.Combine(roaming, @"KuGou8\log"));
        U("WPS 插件缓存", Path.Combine(roaming, @"kingsoft\wps\addons"));
        U("WPS 文档缓存", Path.Combine(roaming, @"kingsoft\office6\cache"));

        // --- user level: games ---
        U("极限竞速4 图形缓存", Path.Combine(local, @"ForzaHorizon4\scratch\GraphicsCache"));
        U("UAGame 日志", Path.Combine(local, @"UAGame\Saved\Logs"));
        U("UAGame CDN 缓存", Path.Combine(local, @"UAGame\Saved\CDNCached"));
        if (gameCaches)
            U("微软模拟飞行 流式缓存", Path.Combine(local, @"Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache"));

        // --- user level: agent runtimes ---
        U("DSH 旧运行时", Path.Combine(user, @".dsh\dsh-runtimes\dsh-primary-runtime.previous"));
        U("DSH 缓存", Path.Combine(user, @".dsh\cache"));
        U("DSH 插件残留", Path.Combine(user, @".dsh\storages\whale-sense"));
        U("DSH 会话项目缓存", Path.Combine(user, @".dsh\storages\session_projcache"));

        // --- system level (requires admin) ---
        S("Windows 安装日志 (Panther)", Path.Combine(win, @"Panther\monitor"));
        S("Windows 安装日志 (UnattendGC)", Path.Combine(win, @"Panther\UnattendGC"));
        S("WinRE 回滚残留 ($WinREAgent)", @"C:\$WinREAgent");
        S("VS 安装包缓存 (vshc)", @"C:\vshc");
        S("Windows 临时目录", Path.Combine(win, "Temp"));
        S("Windows Update 下载缓存", Path.Combine(win, @"SoftwareDistribution\Download"));
        S("预读取 (Prefetch)", Path.Combine(win, "Prefetch"));
        S("CBS 日志", Path.Combine(win, @"Logs\CBS"));
        S("DISM 日志", Path.Combine(win, @"Logs\DISM"));
        S("WER 报告队列", Path.Combine(programData, @"Microsoft\Windows\WER\ReportQueue"));
        S("WER 报告归档", Path.Combine(programData, @"Microsoft\Windows\WER\ReportArchive"));

        // ---------------- run ----------------
        var isAdmin = IsAdmin();
        Console.WriteLine();
        Console.WriteLine($"  disk-cleaner v{Version}   " + (apply ? "模式: 执行清理" : "模式: 预览 (dry-run)"));
        Console.WriteLine("  管理员权限: " + (isAdmin ? "是" : "否"));
        Console.WriteLine("  系统: " + Environment.OSVersion.VersionString);
        Console.WriteLine(new string('-', 82));

        if (apply && !yes)
        {
            Console.Write("  确认执行清理？(y/N) ");
            if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("  已取消。");
                return 0;
            }
        }

        foreach (var t in targets)
        {
            var size = SafeSize(t.Path);
            if (size <= 0) continue;                       // missing or empty

            if (t.Admin && !isAdmin)
            {
                Report.Add((t.Label, size, t.Path, true));
                Console.WriteLine($"  [跳过:需管理员] {t.Label,-34} {Fmt(size),10}  {t.Path}");
                continue;
            }

            if (!apply)
            {
                Report.Add((t.Label, size, t.Path, t.Admin));
                Console.WriteLine($"  [可清] {t.Label,-34} {Fmt(size),10}  {t.Path}");
                continue;
            }

            ClearDir(t.Path);
            var remain = SafeSize(t.Path);
            if (remain < 0) remain = 0;
            var freed = Math.Max(0, size - remain);
            _freedTotal += freed;
            Report.Add((t.Label, freed, t.Path, t.Admin));
            var note = remain > 0 ? $"  (残留 {Fmt(remain)}, 部分文件被占用)" : "";
            Console.WriteLine($"  [已清] {t.Label,-34} {Fmt(freed),10}  {t.Path}{note}");
        }

        // recycle bin (Windows API)
        var rbSize = RecycleBinSize();
        if (rbSize > 0 || apply)
        {
            Report.Add(("回收站", apply ? rbSize : rbSize, "RecycleBin", false));
            if (apply) EmptyRecycleBin();
            Console.WriteLine($"  [{(apply ? "已清" : "可清")}] {"回收站",-34} {Fmt(rbSize),10}");
            if (apply) _freedTotal += rbSize;
        }

        Console.WriteLine(new string('-', 82));
        var planned = Report.Sum(r => r.Bytes);
        if (apply)
            Console.WriteLine($"  本次实际释放: {Fmt(_freedTotal)}   (共 {Report.Count} 项)");
        else
            Console.WriteLine($"  可释放总计: {Fmt(planned)}   (共 {Report.Count} 项)");

        // DISM component cleanup (optional, slow)
        if (dism && apply)
        {
            if (!isAdmin) Console.WriteLine("  [!] DISM 组件清理需要管理员权限，已跳过。");
            else
            {
                Console.WriteLine("  正在执行 DISM 组件清理（可能数分钟）...");
                var psi = new ProcessStartInfo("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup /Quiet")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using var p = Process.Start(psi)!;
                p.WaitForExit();
                Console.WriteLine("  DISM 结束，退出码: " + p.ExitCode);
            }
        }

        WriteReportFile(apply);
        Console.WriteLine();
        if (!apply)
        {
            Console.WriteLine("  这是预览，未删除任何文件。加 --apply 才会执行删除（非管理员会自动弹 UAC 提权）。");
            Console.WriteLine("  加 --game-caches 可额外清理游戏流式缓存；加 --dism 可做组件存储清理。");
        }
        Console.WriteLine();
        return 0;
    }

    // ---------------- helpers ----------------

    private static string Quote(string s) => s.Contains(' ') ? "\"" + s + "\"" : s;

    private static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string Fmt(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
        if (bytes >= 1L << 20) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F0} KB";
        return $"{bytes} B";
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSize(string lpFileName, out uint lpFileSizeHigh);

    /// <summary>Real on-disk allocation of a single file (compressed/sparse aware).</summary>
    private static long AllocatedSize(string file)
    {
        try
        {
            var low = GetCompressedFileSize(file, out var high);
            if (low != uint.MaxValue)
                return (long)(((ulong)high << 32) | low);
            // fallback: logical size
            return new FileInfo(file).Length;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Real allocated size of a directory tree. Never follows reparse points
    /// (junctions / symlinks / cloud placeholders such as CrossDevice mounts).
    /// </summary>
    private static long SafeSize(string path)
    {
        try
        {
            if (File.Exists(path) && !Directory.Exists(path)) return AllocatedSize(path);
            if (!Directory.Exists(path)) return -1;
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return -1;

            long total = 0;
            var stack = new Stack<string>();
            stack.Push(path);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                string[] entries;
                try { entries = Directory.GetFileSystemEntries(dir); } catch { continue; }
                foreach (var e in entries)
                {
                    try
                    {
                        var a = File.GetAttributes(e);
                        if (a.HasFlag(FileAttributes.ReparsePoint)) continue;  // do not follow
                        if (a.HasFlag(FileAttributes.Directory)) stack.Push(e);
                        else total += AllocatedSize(e);
                    }
                    catch { /* locked / access denied */ }
                }
            }
            return total;
        }
        catch { return -1; }
    }

    /// <summary>Delete the CONTENTS of a directory (keeps the directory itself).</summary>
    private static void ClearDir(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return;
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(path); } catch { return; }
            foreach (var entry in entries) DeleteTree(entry);
        }
        catch { /* ignore */ }
    }

    private static void DeleteTree(string path)
    {
        try
        {
            var attr = File.GetAttributes(path);
            if (attr.HasFlag(FileAttributes.ReparsePoint)) return;   // never delete / penetrate mounts
            if (attr.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
            if (attr.HasFlag(FileAttributes.Directory))
            {
                foreach (var child in Directory.GetFileSystemEntries(path)) DeleteTree(child);
                try { Directory.Delete(path); } catch { }
            }
            else
            {
                try { File.Delete(path); } catch { }
            }
        }
        catch { }
    }

    private static void WriteReportFile(bool applied)
    {
        try
        {
            var file = Path.Combine(Path.GetTempPath(), "disk-cleaner-report.txt");
            var sb = new StringBuilder();
            sb.AppendLine($"disk-cleaner v{Version}  {(applied ? "APPLIED" : "DRY-RUN")}  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"admin={IsAdmin()}  os={Environment.OSVersion.VersionString}");
            sb.AppendLine(new string('-', 96));
            foreach (var r in Report.OrderByDescending(r => r.Bytes))
                sb.AppendLine($"{Fmt(r.Bytes),12}  {(r.Admin ? "ADMIN " : "USER  ")}{r.Label,-34} {r.Path}");
            sb.AppendLine(new string('-', 96));
            sb.AppendLine(applied
                ? $"FREED: {Fmt(_freedTotal)}"
                : $"RECLAIMABLE: {Fmt(Report.Sum(r => r.Bytes))}");
            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            Console.WriteLine("  报告已写入: " + file);
        }
        catch { }
    }

    // ---------------- recycle bin (P/Invoke) ----------------

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private static long RecycleBinSize()
    {
        try
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            return SHQueryRecycleBin("C:\\", ref info) == 0 ? info.i64Size : 0;
        }
        catch { return 0; }
    }

    private static void EmptyRecycleBin()
    {
        try { SHEmptyRecycleBin(IntPtr.Zero, "C:\\", 0x1 | 0x2 | 0x4); } catch { }
    }

    private static void PrintHelp()
    {
        Console.WriteLine($$"""
disk-cleaner v{{Version}} - Windows C: 盘缓存/日志清理工具

用法:
  disk-cleaner.exe              预览（默认，不删除任何文件）
  disk-cleaner.exe --apply      执行清理；非管理员时自动请求 UAC 提权
  disk-cleaner.exe --apply --yes        跳过确认
  disk-cleaner.exe --apply --no-elevate 禁止自动提权（只清用户级）
  disk-cleaner.exe --game-caches        额外清理游戏流式缓存（如微软模拟飞行）
  disk-cleaner.exe --dism               清理后执行 DISM 组件存储清理（较慢）

说明:
  - 默认 dry-run，只统计不删除
  - 统计的是真实磁盘占用（稀疏/压缩文件不虚高），不是逻辑文件大小
  - 不跟随 junction / 符号链接 / 云占位符（手机跨设备目录不会被误删）
  - 绝不触碰: 用户文档、下载、桌面、聊天记录、剪辑草稿、Windows\System32、
    WinSxS 文件本体、Windows\Installer、Program Files、驱动
  - 报告写入 %TEMP%\disk-cleaner-report.txt
""");
    }
}
