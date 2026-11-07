# disk-cleaner

Windows C: 盘缓存 / 日志 / 残留清理工具。**默认 dry-run（只统计预览），加 `--apply` 才真正删除。**

打包为单文件自包含 exe（win-x64，无需安装 .NET 运行时）。

## 用法

```powershell
.\disk-cleaner.exe                 # 预览：列出可清理项和大小，不删任何文件
.\disk-cleaner.exe --apply         # 执行清理（需要管理员时自动弹 UAC）
.\disk-cleaner.exe --apply --yes   # 跳过交互确认
.\disk-cleaner.exe --game-caches   # 额外清理游戏流式缓存（微软模拟飞行等）
.\disk-cleaner.exe --dism          # 清理后执行 DISM 组件存储清理
.\disk-cleaner.exe --help
```

清理完成后报告写入 `%TEMP%\disk-cleaner-report.txt`。

## 清理范围

**用户级（无需提权）**

| 类别 | 示例 |
|---|---|
| 临时文件 | `%TEMP%`、CrashDumps、WER |
| 浏览器/开发工具 | Edge Cache、npm、pnpm、pip、D3DSCache |
| 视频剪辑 | 剪映 `User Data\Cache`、CEF 缓存、日志 |
| 腾讯系 | 企业微信升级包、微信 4.x 日志/崩溃转储 |
| 直播/音乐/聊天 | 直播伴侣礼物素材缓存、酷狗封面/升级包、WPS 插件缓存 |
| 游戏 | 极限竞速 4 图形缓存、UAGame 日志、CDN 缓存 |
| Agent 运行时 | Codex / DSH 旧版本残留、缓存目录 |
| 驱动缓存 | NVIDIA 驱动安装包缓存 |

**系统级（需要管理员，自动提权）**

| 类别 | 说明 |
|---|---|
| `C:\Windows\Panther` | Windows 安装/升级监控日志（WinSetupMon，常见数十 GB） |
| `C:\$WinREAgent` | WinRE 更新回滚残留 |
| `C:\vshc` | Visual Studio 安装包缓存 |
| `C:\Windows\Temp` / `SoftwareDistribution\Download` / `Prefetch` | 系统临时区 |
| `C:\Windows\Logs\CBS` / `DISM` | 维护日志 |
| WER 队列、回收站 | — |
| `--dism` | 组件存储清理（WinSxS 被取代组件） |

## 安全边界

工具**绝不触碰**：

- 用户文档 / 下载 / 桌面 / 图片
- 聊天记录数据库（QQ、微信）
- 视频剪辑草稿（剪映 `Projects`）
- `Windows\System32`、`WinSxS` 文件本体、`Windows\Installer`（MSI 缓存，删了卸载/修复会坏）
- `Program Files`、驱动程序、已安装程序本体
- **junction / 符号链接 / 云占位符**：文件和删除逻辑都会跳过 reparse point，所以手机跨设备目录（`CrossDevice` 这类云端占位符）不会被误删

## 从源码构建

```powershell
dotnet publish src\disk-cleaner.csproj -c Release -o dist
```

要求 .NET 9 SDK。产物 `dist\disk-cleaner.exe` 为单文件自包含版本。

## License

MIT
