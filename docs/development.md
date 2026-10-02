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
| 诊断日志 | DiagnosticLogTests.cs | 容量限制、滚动、单行输出与不可写位置 |

当前共 89 项检查。共用断言位于 TestSuite.cs，共用 HTTP、时钟、缓存和数据源替身位于 SyncTestData.cs。测试数据按数据格式版本识别，与开发阶段无关。

## 界面与桌面检查

```powershell
.\scripts\build.ps1 -Publish -RunUiChecks -RunDesktopChecks
```

- `PrototypeChecks` 是现有 WPF 场景检查入口，覆盖倒计时、到点、未知时间、布局溢出和绑定错误，共 13 项。
- `DesktopChecks` 覆盖卡片固定高度、统一图标、主题、置顶、模式、阅读位置、托盘、退出与关于，共 46 项。
- 模拟场景只由自动检查参数注入，产品界面不提供演示菜单。
- 100%／200% 工作区模拟与注入时区不能替代真实系统设置变更、显示器插拔或休眠验证。

## 发布与解压包验证

```powershell
.\scripts\build.ps1 -Publish -RunUiChecks -RunDesktopChecks -RunLiveChecks
.\scripts\verify-package.ps1 -ArchivePath artifacts\releases\CodexResetWidget-1.0.0-rc.1-win-x64.zip -RunDesktopChecks
```

发布脚本从 EXE 读取版本，创建新的 staging 目录并生成自包含 ZIP。包内包含项目许可、运行时包原始许可、使用说明和 RELEASE.json；包外提供 SHA256 校验文件。

验证脚本解压 ZIP 到含中文和空格的新目录，检查版本、依赖、许可、文件大小与哈希。启动检查验证实际运行时来自解压目录，并比较运行前后包内文件；报告中明确记录未验证的干净 Windows 环境。

`-RunLiveChecks` 使用真实公开数据通路，检查公告、完整历史和刷新后的阅读保持。需要验证升级时追加 `-UpgradeDataDirectory <测试用户数据目录>`；该目录必须包含 settings.json 与 cache/snapshot.json，脚本先复制，再用副本检查保存的模式、主题、置顶、宽度和缓存兼容。

生成物与报告位于 `artifacts/releases/`，不提交到 Git。当前候选版的结果和未验证项见 [验证记录](validation.md)，用户操作与升级说明见 [使用说明](release-usage.txt)。

## 后续工作

当前发布候选版仍需最终用户验收，以及干净 Windows 11 x64、实际多屏、系统 DPI／时区热切换和真实休眠恢复的现场验证。自启动、通知、历史筛选、备用数据源、ARM64 与安装包未进入当前实现。
