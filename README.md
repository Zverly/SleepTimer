# 睡前定时关机

面向 Windows 10/11 的离线定时关机与睡眠工具，采用暗色、鼠标优先界面。应用不依赖网络，运行数据保存在程序目录的 `data` 文件夹中，适合便携使用。

适合需要临时安排关机、睡眠，或希望在关闭应用后仍保留计划的 Windows 用户。

![SleepTimer 图标](assets/generated/sleep-timer-icon-reference-preview-v3.png)

## 功能

- 正常关机、强制关机和睡眠三种动作。
- 30、60、90、120 分钟快捷预设，也可输入具体时间。
- 倒计时页面支持增加或减少 30 分钟、取消和隐藏到托盘。
- 托盘支持显示窗口、增加或减少 30 分钟、取消计划、打开设置，以及退出并保留或取消计划。
- 支持托盘快速设置定时任务，记录任务历史、操作日志和诊断信息。
- 支持 10 分钟、1 分钟和 30 秒提醒，以及开机自启动和静默启动。
- Windows 计划任务保证窗口关闭或程序退出后任务仍可执行。
- 单实例运行；再次启动会激活已有窗口。

## 项目结构

- `src/`：应用、核心领域逻辑和 Windows 平台实现。
- `tests/`：核心、应用和 Windows 适配层测试。
- `assets/`：正式图标及设计资源。
- `scripts/`：构建、发布和图标资源处理脚本。
- `docs/`：设计说明、验收清单和开发计划。

## 快速开始

### 使用源码

要求：Windows 10/11、.NET 8 SDK，以及 PowerShell。

```powershell
git clone https://github.com/Zverly/SleepTimer.git
cd SleepTimer
dotnet restore SleepTimer.sln
dotnet run --project src/SleepTimer.App/SleepTimer.App.csproj --configuration Release
```

### 使用发布版

发布版建议放在 GitHub Releases 中。首次运行时请保留程序目录结构；应用会在程序目录下创建 `data` 文件夹保存设置和运行记录。

## 安全说明

强制关机会关闭阻止关机的程序，可能造成未保存数据丢失，仅在明确勾选后使用。正常关机不会自动升级为强制关机。项目测试全部使用进程替身，不会执行真实关机、睡眠或计划任务。

## 构建与测试

在 Windows 10/11 上安装 .NET 8 SDK 后运行：

```powershell
dotnet test SleepTimer.sln --configuration Release
dotnet build SleepTimer.sln --configuration Release
```

## 发布

生成 x64 自包含单文件：

```powershell
dotnet publish src/SleepTimer.App/SleepTimer.App.csproj --configuration Release --runtime win-x64 --self-contained true --output artifacts/win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

项目中的 `scripts/publish.ps1` 还会执行测试、PE 架构、单文件运行时和 ZIP 内容审计；它是便携开发环境脚本，运行时需要项目根目录存在 `.dotnet/dotnet.exe`，该 SDK 不随 GitHub 源码上传。

发布脚本会审计 x64 PE、自包含单文件运行时、调试/测试污染物，以及 ZIP 与发布目录的 SHA-256 清单一致性。应用的偏好、任务摘要、历史记录和诊断日志保存在便携目录的 `data` 子目录，不使用 C 盘用户配置目录；移动应用时请保留整个目录结构，不要只移动 EXE。

发布审计不会创建 Windows 计划任务，也不会执行真实关机或睡眠。真实 Windows 行为请按 [Windows 集成验收清单](tests/SleepTimer.Windows.Tests/WindowsIntegrationChecklist.md) 手工验证。

## 数据与隐私

- 设置、当前任务和历史记录写入程序目录下的 `data` 文件夹。
- 日志写入 `data/logs`；启动异常写入 `data/startup-error.log`。
- 应用不主动联网，也不会把任务、日志或个人数据上传到第三方服务。
- `data`、日志和发布产物不应提交到 GitHub。

## 已知限制与验证

自动测试主要验证命令参数、持久化和领域逻辑。首次真实使用前，应在 Windows 测试账户中验证任务计划程序权限、托盘交互、睡眠唤醒、开机自启动和关机提醒。真实电源动作请先保存未完成的工作，并按 [Windows 集成验收清单](tests/SleepTimer.Windows.Tests/WindowsIntegrationChecklist.md) 手工验证。

## 许可证

当前仓库尚未声明开源许可证。补充 `LICENSE` 文件后，其他人才能明确了解项目的使用、修改和再分发权限。
