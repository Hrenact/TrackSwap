# TrackSwap · 位姿映射

TrackSwap 是一个轻量的 Windows 工具，用来管理 SteamVR 的设备位姿映射。

当前版本：**TrackSwap v002**

简单来说，它可以让一个设备负责提供位置和旋转数据，同时继续使用另一个设备的按键输入。例如：

- 使用 VIVE Tracker 的定位，保留一体机手柄的按键和摇杆
- 用左手柄的位置暂时替代右手柄
- 用 Tracker 替代头显或其他在线设备的位姿
- 同时保存多条互不冲突的映射规则

TrackSwap 只调整定位来源，不会把来源设备的按键映射给目标设备。

## 系统要求

- Windows 10 或 Windows 11（64 位）
- SteamVR
- .NET Framework 4.8

程序直接使用 SteamVR 自带的 OpenVR 运行库，不需要单独安装 OpenVR SDK。

## 安装与启动

TrackSwap 不需要安装。解压完整程序目录后，先完全退出 SteamVR，以
PowerShell 运行 `scripts\Install-Driver.ps1` 注册随附驱动，再启动 SteamVR、
`runtime\TrackSwap.Runtime.exe --run` 和 `TrackSwap.exe`。UI 也能在 Runtime 未运行时
从程序目录启动它。

请使用 GitHub Release 提供的完整压缩包，不要只复制 EXE。首次启动时，程序会自动寻找本机 SteamVR 的配置文件和运行目录。

## 使用方法

1. 打开需要使用的手柄、Tracker 和头显。
2. 启动 SteamVR，等待设备全部连接。
3. 启动 TrackSwap；如果设备列表没有更新，点击“刷新”。
4. 在“追踪来源”中选择负责提供定位的设备。
5. 在“替换目标”中选择要接收该位姿的角色或具体设备。
6. 完全退出 SteamVR。
7. 等待顶部状态变为“SteamVR 已退出”，然后点击“应用配置”。
8. 重新启动 SteamVR，检查定位与按键输入是否符合预期。

> SteamVR 运行时可以查看和编辑选择，但 TrackSwap 会拒绝写入配置，避免 SteamVR 退出时覆盖修改结果。

## 追踪来源与替换目标

“追踪来源”是提供位置和旋转数据的设备。

“替换目标”是使用这份定位数据的设备。目标可以是：

- 右手、左手或头显
- 当前由 SteamVR 枚举到的具体在线设备
- 配置中曾经保存过的设备

头显、左手和右手是 OpenVR 明确支持的常用目标。具体设备之间的替换属于实验性用法，实际效果可能受到设备驱动和 SteamVR 版本影响。

TrackSwap 会阻止来源覆盖自身和循环覆盖。同一个来源或同一个目标只能保留一条有效规则；再次应用时，冲突的旧规则会被替换。

## 管理当前配置

“当前配置”区域会显示所有已应用的位姿规则。

- “编辑”会把规则载入上方编辑器
- “移除”只删除选中的规则
- 多条不冲突的规则可以同时存在

移除规则同样要求 SteamVR 已完全退出，并且操作前会自动备份配置。

## 备份与恢复

每次应用、移除或恢复规则前，TrackSwap 都会在 `steamvr.vrsettings` 旁创建带时间戳的备份。

点击底部的“恢复备份”可以查看历史备份中的规则。恢复时：

- 只恢复 `TrackingOverrides` 位姿映射
- 保留当前画质、驱动等其他 SteamVR 设置
- 恢复前再次保存当前配置，方便撤销恢复操作
- 损坏或无法解析的备份不会被应用

“定位配置文件”可以在资源管理器中直接选中当前的 `steamvr.vrsettings`。

## 注意事项

- 修改配置前请完全退出 SteamVR。
- 位姿映射不会自动校准设备之间的物理位置和朝向偏移。
- 来源与目标可能在 SteamVR 中显示为相同位置，这是 `TrackingOverrides` 的正常行为。
- 如果结果异常，可以移除对应规则，或使用“恢复备份”回到先前状态。
- 不同驱动提供的坐标空间可能不一致；跨定位系统组合不一定能够直接使用。

## 文件位置

TrackSwap 会通过 OpenVR 注册信息自动查找 SteamVR 配置。常见位置为：

```text
<Steam 安装目录>\config\steamvr.vrsettings
```

自动备份保存在同一目录，文件名类似：

```text
steamvr.vrsettings.trackswap-20260917-114817-123.backup
```

## 开发与构建

项目使用 WPF、.NET Framework 4.8 和 Newtonsoft.Json。

`main` 保留稳定的 v001 历史。v002 的 Runtime、共享协议和原生 OpenVR
驱动在 `v002-runtime` 分支开发，架构契约见
[`docs/v002-architecture.md`](docs/v002-architecture.md)，驱动开发闭环见
[`docs/driver-development.md`](docs/driver-development.md)。普通构建不会安装或注册驱动。

当前 `v002-runtime` 开发界面把功能明确分为两个页签：

- “运行时路由 v002”通过独立 Runtime 热切换物理来源、编辑局部刚体偏移，
  并显示 Runtime、驱动连接以及配置 revision 的待应用/已应用状态；
- “静态覆盖 v001”保留原有 `TrackingOverrides`、备份和恢复流程。静态映射
  仍然只负责把稳定虚拟代理引导到目标，不提供偏移。

开发环境启动 Runtime：

```powershell
dotnet run --project src\TrackSwap.Runtime\TrackSwap.Runtime.csproj -c Release -- --run
```

Runtime 默认把原子配置快照保存在
`%LOCALAPPDATA%\TrackSwap\runtime-config.json`。UI 也可在离线状态下显式启动
同目录随附的 `TrackSwap.Runtime.exe`；关闭 UI 不会停止 Runtime。

“自动校准”可同时采集物理来源与原始物理目标，自动计算并保存局部位置和
旋转偏移档案。它只改变偏移，不会把参考目标切换为新的物理位姿来源；要让
虚拟设备直接跟随另一设备，请在运行时路由中选择该设备并应用单位偏移。它不
替代 Space Calibrator 等跨追踪空间对齐工具，而且采集前必须先停用虚拟代理的
静态覆盖。完整流程和失败恢复见
[`docs/calibration.md`](docs/calibration.md)。

“实时 3D 位姿预览”以约 30 Hz 显示物理来源、变换后虚拟输出、目标和各自
坐标轴。遥测读取的是驱动发布的副本，UI 关闭、卡顿或断开不会影响 SteamVR
位姿提交。详见 [`docs/pose-preview.md`](docs/pose-preview.md)。

Debug 构建：

```powershell
dotnet build TrackSwap.sln
```

Release 构建：

```powershell
dotnet build TrackSwap.sln -c Release
```

生成完整的 Windows x64 发布包、ZIP 和 SHA-256 文件：

```powershell
.\scripts\Build-Release.ps1 -Version v002 -Clean
```

升级到不同目录中的完整包时，应先完全退出 SteamVR，再运行
`scripts\Install-Driver.ps1 -ReplaceExisting`；脚本注册失败时会尝试恢复旧路径。
卸载驱动运行 `scripts\Uninstall-Driver.ps1`。这些脚本只更改 OpenVR 驱动注册，
不会静默删除用户在 `%LOCALAPPDATA%\TrackSwap` 中保存的配置和校准档案。

生成的程序位于：

```text
src\TrackSwap\bin\Release\net48\TrackSwap.exe
```

## 版本规则

TrackSwap 使用连续编号：`v001`、`v002`、`v003`……不区分主版本、次版本或预发布状态。

推送与项目版本一致的 `vNNN` Git 标签后，GitHub Actions 会自动构建 Windows x64 Release、生成 ZIP 压缩包和 SHA-256 校验文件，并发布对应的 GitHub Release。

## 许可证与声明

TrackSwap 由 Hrenact 以 [MIT License](LICENSE) 开源。

第三方组件及其许可证请参阅 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。TrackSwap 是非官方项目，与 Valve Corporation 或 SteamVR 没有隶属、授权或认可关系。
