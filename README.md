# TrackSwap · 位姿映射

TrackSwap 是一个轻量的 Windows 工具，用来管理 SteamVR 的设备位姿映射。

当前版本：**TrackSwap v003**

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
2. 启动 SteamVR，等待设备全部连接，然后启动 Runtime 与 TrackSwap。
3. 点击侧边栏“新增”，为路由选择物理位姿来源和替换目标。
4. 根据需要编辑局部位置、旋转偏移，或在静态覆盖尚未启用时执行自动校准。
5. 点击“应用更改”。Runtime 会立即保存路由并让对应虚拟代理开始跟随来源。
6. 如果界面提示代理尚未绑定目标，请完全退出 SteamVR，并对该配置再次点击“应用更改”。
7. 重新启动 SteamVR，检查定位与原目标设备的按键输入是否符合预期。

代理与目标完成首次静态绑定后，切换物理来源和修改偏移可以在 SteamVR 运行期间立即生效，不需要重启。写入、移除或更换 `TrackingOverrides` 引导映射仍然要求 SteamVR 完全退出。

## 追踪来源与替换目标

“追踪来源”是提供位置和旋转数据的设备。

“替换目标”是使用这份定位数据的设备。目标可以是：

- 右手、左手或头显
- 当前由 SteamVR 枚举到的具体在线设备
- 配置中曾经保存过的设备

头显、左手和右手是 OpenVR 明确支持的常用目标。具体设备之间的替换属于实验性用法，实际效果可能受到设备驱动和 SteamVR 版本影响。

TrackSwap 会阻止来源覆盖自身、循环覆盖、目标冲突，以及“右手柄 → 右手”等角色自引用路由。角色自引用会让 SteamVR 把已覆盖的目标位姿反馈为来源并冻结在上一帧。

## 管理当前配置

侧边栏列出所有命名路由。每条路由使用一个稳定虚拟代理，最多支持八条：`TRKSWAP-PROXY-00` 至 `TRKSWAP-PROXY-07`。

- “新增”分配当前最低的空闲代理槽位
- 右键配置可以重命名、启用或停用，以及删除
- 每条配置独立保存来源、目标、偏移和校准档案选择
- 3D 预览始终显示当前配置的来源、虚拟输出与目标
- 多条目标互不冲突的路由可以同时运行

首次绑定、目标变更、停用清理和删除清理都可能需要修改静态引导映射，因此应在 SteamVR 完全退出时执行。

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
- Runtime 路由支持局部位置和旋转偏移；旧版静态覆盖本身不支持偏移。
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

`main` 保留稳定的 v001 历史。v002 已发布；v003 的多路由扩展继续在
`v002-runtime` 分支开发，架构契约见
[`docs/v002-architecture.md`](docs/v002-architecture.md)，驱动开发闭环见
[`docs/driver-development.md`](docs/driver-development.md)。普通构建不会安装或注册驱动。

当前界面以侧边栏管理命名运行时路由，并在“设置”中保留 v001
`TrackingOverrides`、备份和恢复流程。静态映射仍然只负责把稳定虚拟代理
引导到目标，不提供偏移。

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
.\scripts\Build-Release.ps1 -Version v003 -Clean
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
