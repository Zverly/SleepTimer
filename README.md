# 睡前定时关机

面向 Windows 10/11 的离线定时关机与睡眠工具，采用暗色、鼠标优先界面。应用不依赖网络，运行数据保存在程序目录的 `data` 文件夹中，适合便携使用。

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

## 安全说明

强制关机会关闭阻止关机的程序，可能造成未保存数据丢失，仅在明确勾选后使用。正常关机不会自动升级为强制关机。项目测试全部使用进程替身，不会执行真实关机、睡眠或计划任务。

## 构建与测试

项目自带的 .NET SDK 位于 E 盘 `.dotnet` 目录。运行：

```powershell
E:\codex_project\.dotnet\dotnet.exe test E:\codex_project\SleepTimer.sln --configuration Release
E:\codex_project\.dotnet\dotnet.exe build E:\codex_project\SleepTimer.sln --configuration Release
```

## 发布

```powershell
powershell -ExecutionPolicy Bypass -File E:\codex_project\scripts\publish.ps1
```

产物位于：

- `E:\codex_project\artifacts\win-x64`
- `E:\codex_project\artifacts\SleepTimer-win-x64.zip`

发布脚本会审计 x64 PE、自包含单文件运行时、调试/测试污染物，以及 ZIP 与发布目录的 SHA-256 清单一致性。应用的偏好、任务摘要、历史记录和诊断日志保存在便携目录的 `data` 子目录，不使用 C 盘用户配置目录；移动应用时请保留整个目录结构，不要只移动 EXE。

发布审计不会创建 Windows 计划任务，也不会执行真实关机或睡眠。真实 Windows 行为请按 [Windows 集成验收清单](tests/SleepTimer.Windows.Tests/WindowsIntegrationChecklist.md) 手工验证。

## 集成测试限制

自动测试只验证命令参数和领域逻辑。首次真实使用前，应在 Windows 测试账户中验证任务计划程序权限、托盘交互、睡眠唤醒和关机提醒；保存所有文档后再做真实电源动作验收。
