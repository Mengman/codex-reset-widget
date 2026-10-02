# 开发与测试

当前实现使用 C#、WPF、.NET 10，面向 Windows 11 x64。SDK 由 global.json 锁定为 10.0.401；构建脚本优先使用 `.tools/dotnet/dotnet.exe`，否则使用系统 dotnet。

## 编译与功能测试

```powershell
.\scripts\build.ps1
```

脚本执行依赖恢复、Release 编译与测试程序。测试程序使用注入时钟、内存数据源、固定格式文件及隔离临时目录，不依赖实时网络，也不修改用户的正式设置或缓存。

`tests/CodexResetWidget.Tests/Program.cs` 只负责启动功能分组和汇总结果。任一检查失败时，程序返回非零退出码；`TestSuite.cs` 统一输出功能组、名称和结果。

| 功能组 | 文件 | 覆盖范围 |
| --- | --- | --- |
| 重置状态 | ResetStateTests.cs | 未来预告、缺少时间、观察信号、到点回顾、预告消失与更正 |
| 时间显示 | TimeDisplayTests.cs | 本地日期、UTC 偏移、非整小时、夏令时和倒计时边界 |
| 日历 | CalendarTests.cs | 周一开始、闰年、四至六行、跨月归档、多事件和预告叠加 |
| 场景数据 | ScenarioDataTests.cs | 场景完整性与缺少来源时不伪造作者 |
| API 客户端 | ApiClientTests.cs | 字段校验、未知类型、304、429、取消、分页与部分失败 |
| 同步控制 | SyncControllerTests.cs | 独立周期、退避、乱序、缓存恢复、刷新合并、退出与过期状态 |
| JSON 缓存 | JsonCacheStoreTests.cs | 持久化、取消写入、损坏文件与不兼容结构 |
| 窗口位置 | WindowPlacementTests.cs | DPI 换算、负坐标、工作区限制和缺失显示器 |
| 设置存储 | SettingsStoreTests.cs | 默认设置、持久化、坏文件、非法保存和写入失败 |
| 格式兼容 | StorageCompatibilityTests.cs | 格式版本 1 的设置恢复与旧缓存离线跨目标时间 |
| 国际化 | LocalizationTests.cs | 系统语言映射、双语资源、日期格式、实时切换通知、旧消息翻译与设置保存 |
| 诊断日志 | DiagnosticLogTests.cs | 容量限制、滚动、单行输出与不可写位置 |

当前共 110 项检查。共用断言位于 TestSuite.cs，共用 HTTP、时钟、缓存和数据源替身位于 SyncTestData.cs。测试数据按数据格式版本识别，与开发阶段无关。

## 界面与桌面检查

```powershell
.\scripts\build.ps1 -Publish -RunUiChecks -RunDesktopChecks
```

- `PrototypeChecks` 是现有 WPF 场景检查入口，覆盖倒计时、到点、未知时间、布局溢出和绑定错误，共 13 项。
- `DesktopChecks` 覆盖卡片固定高度、统一图标、主题、置顶、模式、阅读位置、托盘、退出与关于，共 60 项。
- 模拟场景只由自动检查参数注入，产品界面不提供演示菜单。
- 100%／200% 工作区模拟与注入时区不能替代真实系统设置变更、显示器插拔或休眠验证。

## 发布与解压包验证

```powershell
.\scripts\build.ps1 -Publish -RunUiChecks -RunDesktopChecks -RunLiveChecks
.\scripts\verify-package.ps1 -ArchivePath artifacts\releases\CodexResetWidget-1.0.0-rc.2-win-x64.zip -RunDesktopChecks
```

发布脚本从 EXE 读取版本，创建新的 staging 目录并生成自包含 ZIP。包内包含项目许可、运行时包原始许可、使用说明和 RELEASE.json；包外提供 SHA256 校验文件。

验证脚本解压 ZIP 到含中文和空格的新目录，检查版本、依赖、许可、文件大小与哈希。启动检查验证实际运行时来自解压目录，并比较运行前后包内文件；报告中明确记录未验证的干净 Windows 环境。

`-RunLiveChecks` 使用真实公开数据通路，检查公告、完整历史和刷新后的阅读保持。需要验证升级时追加 `-UpgradeDataDirectory <测试用户数据目录>`；该目录必须包含 settings.json 与 cache/snapshot.json，脚本先复制，再用副本检查保存的模式、主题、置顶、宽度和缓存兼容。

生成物与报告位于 `artifacts/releases/`，不提交到 Git。当前候选版的结果和未验证项见 [验证记录](validation.md)，用户操作与升级说明见 [使用说明](release-usage.txt)。

## 后续工作

当前发布候选版仍需最终用户验收，以及干净 Windows 11 x64、实际多屏、系统 DPI／时区热切换和真实休眠恢复的现场验证。自启动、通知、历史筛选、备用数据源、ARM64 与安装包未进入当前实现。

## GitHub Actions

`.github/workflows/ci.yml` 在 main 提交、PR 和手动触发时，在 windows-2025 上安装 global.json 指定 SDK，执行 `build.ps1 -Publish -RunUiChecks`。它复用功能测试、13 项 WPF 场景及解压包校验，不依赖第三方实时数据。工作流产物保留 14 天，失败时仍尝试保留报告。真实 Windows 11 桌面、托盘、显示器和联网验证在本地进行。

`.github/workflows/release.yml` 由推送 v* tag 触发，使用 release-version.ps1 校验 SemVer 和 .NET 版本范围。合法 tag 为 v1.0.0、v1.0.1-rc.1 等；缺少 v、前导零、构建元数据、参数字符或超出程序集版本范围均拒绝。

版本由 `build.ps1 -Version <版本>` 传入 MSBuild，同时覆盖 Version 和 InformationalVersion。程序集数值版本取基础版本，关于显示完整预发布后缀；文件名、README、RELEASE.json 与 tag 一致。未传 Version 的本地构建继续使用 csproj 中的版本。

Windows 构建任务只具有 contents: read 权限，将验证后的 ZIP 和 SHA256 上传。依赖该任务的 Ubuntu 发布任务重新核对 SHA256，并使用内置 GITHUB_TOKEN 和 contents: write 创建草稿、上传附件、公开 Release。带预发布后缀标为 Prerelease，正式 tag 标为普通 Release。任务失败时不进入后续发布步骤；草稿上传失败可重跑，同名附件会替换。项目不需要额外个人 Token。

工作流使用固定提交号的官方 checkout、setup-dotnet、upload-artifact 和 download-artifact Actions。tag 经环境变量传入脚本，校验后用于版本参数，避免将 ref 直接插入命令文本。

```powershell
.\scripts\build.ps1 -Publish -RunUiChecks -Version 1.0.0
# 提交工作流后，在待发布的提交上打 tag：
git tag -a v1.0.0 -m "Release 1.0.0"
git push origin v1.0.0
```

本地验证已使用 1.2.3-rc.4 覆盖版本完成 110 项功能、13 项 WPF 场景和 479 个包文件校验，确认 EXE 数值及产品版本、双语使用说明、清单与 ZIP 名称一致。16 个 tag 校验案例及 actionlint 校验通过；发布命令通过 5 个模拟 CLI 案例，覆盖正式版、预发布、重跑、创建失败和上传失败后停止发布。尚未推送工作流或创建真实 tag；GitHub 托管运行与公开 Release 需在首次推送后验证。
