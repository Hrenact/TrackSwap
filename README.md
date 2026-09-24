# TrackSwap · 位姿映射

TrackSwap 是一个轻量的 Windows 工具，用来管理 SteamVR 的设备位姿映射。

当前版本：**TrackSwap v009**

简单来说，它可以把一个设备的位置和旋转数据输出为虚拟 Tracker、替换另一个设备的位姿，或驱动一对虚拟控制器。例如：

- 把 VIVE Tracker 直接输出为稳定的 TrackSwap 虚拟 Tracker
- 使用 VIVE Tracker 的定位，保留一体机手柄的按键和摇杆
- 用左手柄的位置暂时替代右手柄
- 用 Tracker 位姿驱动虚拟左手或右手控制器，并通过 OSC 或 XInput 手柄提供基础输入
- 同时保存多条互不冲突的映射规则

目标替换模式只调整定位来源，不会把来源设备的按键映射给目标设备。虚拟控制器模式可选择不接收输入，或从 OSC、XInput 接收输入。

当前开发版统一以厘米保存局部位置偏移。旧配置不会自动换算：升级后原有位置数值会直接按厘米解释，请重新检查并输入需要的偏移。

默认情况下，一台物理设备只能作为一条路由的位姿来源。临时测试需要让同一设备同时驱动多条路由时，可在“设置 → 高级选项”启用“允许多条配置使用同一个位姿来源”；该选项不会放宽重复左右手、代理槽、替换目标或循环路由检查。

虚拟控制器的 SteamVR 手部选择优先级默认为 `0`，不会由 TrackSwap 暗中抬高。需要与会注册幽灵手柄的厂商驱动共存时，可在“设置 → 高级选项”输入自定义整数，并随时恢复默认值。

## 系统要求

- Windows 10 或 Windows 11（64 位）
- SteamVR
- .NET Framework 4.8

程序直接使用 SteamVR 自带的 OpenVR 运行库，不需要单独安装 OpenVR SDK。

## 安装与启动

推荐使用 GitHub Release 提供的 Windows 安装程序。安装前先完全退出 SteamVR，安装向导会
复制完整程序、注册随附驱动，并可按需创建桌面快捷方式。安装完成后可直接打开 TrackSwap。
Runtime 会按照“设置 → 高级选项 → Runtime 启停行为”自动由 TrackSwap 或 SteamVR
会话托管，无需日常手动启动。

如需连同控制界面一起自动管理，可在“设置 → 高级选项”启用
“TrackSwap 跟随 SteamVR 启停”。TrackSwap 会注册到 SteamVR 的启动应用列表；首次注册
尚未被当前 SteamVR 会话载入时，驱动与 Runtime 会为本次会话补充启动。

仍需便携使用时，可以下载完整 ZIP；不要只复制 EXE。解压后完全退出 SteamVR，以
PowerShell 运行 `scripts\Install-Driver.ps1` 注册驱动。首次启动时，程序会自动寻找本机
SteamVR 的配置文件和运行目录。

## 使用方法

1. 打开需要使用的手柄、Tracker 和头显。
2. 启动 SteamVR，等待设备全部连接，然后启动 TrackSwap。
3. 点击侧边栏“新增”，选择运行模式与物理位姿来源。目标替换模式还需要选择明确目标；虚拟控制器模式需要选择左右手及控制输入来源。
4. 根据需要在左侧以厘米精确输入局部位置和旋转偏移，或在右侧 3D 预览中使用“调整工具”。
5. 配置完整后，下拉选择会立即保存；数值输入会在停止输入片刻后自动保存。页面右上角显示“配置已同步”即表示 Runtime 已接收更改。
   位姿偏移只接受阿拉伯数字、小数点和必要的前导负号；输入不完整时会留在本地等待继续编辑，完整配置若未通过校验则会用中文弹窗说明原因，并在下一次修改后重新尝试。
6. 只有目标替换模式可能提示“待映射”。此时保持 TrackSwap 打开并完全退出 SteamVR；程序会自动写入静态映射，不需要额外操作。
7. 重新启动 SteamVR，检查虚拟输出或目标设备的定位与输入是否符合预期。

直接输出与虚拟控制器模式不需要 `TrackingOverrides`。目标替换模式完成首次静态绑定后，切换物理来源和修改偏移可以在 SteamVR 运行期间立即生效，不需要重启；写入、移除或更换引导映射仍然要求 SteamVR 完全退出。

## 运行模式

- **输出为虚拟追踪器**：把来源位姿和局部偏移直接输出到稳定的 `TRKSWAP-TRACKER-00` 至 `TRKSWAP-TRACKER-15`，不需要替换实体设备。
- **输出为虚拟控制器**：最多创建一只左手和一只右手虚拟控制器。物理设备提供位姿，控制输入可选“无”、“OSC”或“XInput”。选择“无”时所有按键和模拟量保持中立。
- **替换现有设备位姿（实验性）**：由代理覆盖明确目标的位姿，同时保留目标原有输入。该模式需要 SteamVR 的 `TrackingOverrides` 静态映射，实际效果可能受目标设备驱动影响。

## 追踪来源与替换目标

“追踪来源”是提供位置和旋转数据的设备。

“替换目标”是使用这份定位数据的设备。运行时路由的目标可以是：

- 当前由 SteamVR 枚举到的具体在线设备
- 现有配置中已经保存的具体设备

运行时路由不再提供 `/user/hand/left`、`/user/hand/right` 等 SteamVR 角色作为新目标，避免多个控制器同时连接时的归属歧义。旧配置中的角色目标仍可读取和删除，但必须改选明确的实体设备后才能再次应用或启用。旧版静态映射页仍保留 SteamVR 角色目标。

> [!WARNING]
> 具体设备之间的 `TrackingOverrides` 属于实验性用法，实际结果取决于 SteamVR 版本和目标设备驱动。当前硬件测试中，Valve Index Knuckles 的实体设备目标可正常跟随，而 Pico 手柄的实体设备目标可能忽略覆盖或无法跟随。这不代表所有 Pico 驱动版本都会失败，但请在使用前通过 SteamVR 和 TrackSwap 预览确认结果。TrackSwap 不会静默回退到角色目标。

TrackSwap 会阻止来源覆盖自身、循环覆盖、目标冲突，以及“右手柄 → 右手”等角色自引用路由。角色自引用会让 SteamVR 把已覆盖的目标位姿反馈为来源并冻结在上一帧。

## 管理当前配置

侧边栏最多容纳十六条命名配置，并显示当前数量与总上限。直接输出模式使用 `TRKSWAP-TRACKER-00` 至 `TRKSWAP-TRACKER-15`；替换现有设备位姿模式使用独立的 `TRKSWAP-PROXY-00` 至 `TRKSWAP-PROXY-15` 作为静态映射代理。虚拟控制器仍固定为左右各一只。

- “新增”分配当前最低的空闲代理槽位
- 右键配置可以重命名、启用或停用，以及删除
- 每条配置独立保存来源、目标、偏移和校准档案选择
- 3D 预览始终显示当前配置的物理来源与最终目标；虚拟代理仅作为兼容层，不渲染为第三个用户设备
- 多条目标互不冲突的路由可以同时运行

每条配置可启用“拆分来源”，分别选择位置来源与旋转来源；两个来源会从同一批 OpenVR 位姿中采样，再统一应用该配置的局部偏移。任一来源失效时输出会变为无效，不会静默回退。

高级选项中的实体来源隐藏功能可把配置使用的物理来源移出正常游玩范围，避免替换位姿时额外出现可绑定设备。该功能默认关闭，会在首次启用时提示与其它定位修改软件冲突的风险；代理设备本身使用非穿戴设备类别，不会作为额外 Tracker 暴露给游戏。

SteamVR 运行时删除配置会先进入“待删除”状态，代理继续输出，避免目标立即失去定位。完全退出 SteamVR 后，TrackSwap 会自动清理静态绑定并最终删除；完成前可通过右键菜单取消删除。首次绑定、目标变更和停用清理仍可能要求 SteamVR 完全退出。

## OSC 虚拟控制器输入

在“设置 → OSC”中可以选择地址、接收端口与震动反馈发送端口。只要存在已启用且选择 OSC 的虚拟控制器配置，接收器就会自动启用；不再需要单独的开关。默认仅监听 `127.0.0.1:9015`。OSC 本身没有身份验证或加密，不建议在不可信网络接口上监听。接收端口冲突和本地发送失败会通过底部 OSC 红色状态提示说明。

左、右手使用固定的 `/trackswap/left/` 与 `/trackswap/right/` 参数前缀，采用 Oculus Touch 的标准面键布局：左手为 X/Y，右手为 A/B；两手均支持摇杆 X/Y 与按下、扳机值、抓握值以及菜单键。扳机和抓握只公开连续值与触摸状态，不再公开合成的 Click；两手菜单键按逻辑 OR 合并到左手 System，避免一侧松开覆盖另一侧仍按住的状态。完整参数地址会显示在设置页中，可选中复制但不可修改，也不会写入 `runtime-config.json`。设置页的状态条和摇杆十字轴以约 30 Hz 显示收到的值。

OSC 还会把左右虚拟控制器的 SteamVR 震动事件分别发送到固定的 `/trackswap/left/haptic` 与 `/trackswap/right/haptic`。设置页提供波形预览和测试序列；同一数据包中的控制输入会合并为每只手最多一次驱动快照，重复保活值不会产生多余 IPC。

虚拟控制器采用 Oculus Touch 兼容输入档案，并根据 Touch 的控件语义合成标准 SteamVR 手部骨骼：摇杆、X/Y 或 A/B 与菜单键分别给出不同的拇指落点，扳机驱动食指，抓握值驱动中指、无名指和小指；其余四指还会从伸直时略微张开，随弯曲逐步收拢。TrackSwap 会同时发布 Touch 风格的按钮、摇杆、扳机和抓握触摸状态；OSC 与 XInput 都提供左右手独立的拇指、食指触摸辅助以及“默认接触 / 默认抬起”，实际控制输入始终优先。

SteamVR 中的设备身份保持为独立的 `TrackSwap Controller`，同时声明为 Oculus Touch 兼容控制器，并复用本机 SteamVR 随 Oculus 驱动提供的旧版绑定、控制器图示和 Quest 2 控制器模型。TrackSwap 不再携带 VRChat 专用绑定；支持 Touch 的游戏会直接使用自己的 Touch 绑定。

## XInput 虚拟控制器输入

选择“XInput”后，Runtime 会在后台使用编号最小的已连接 XInput 手柄。默认映射把一只标准 Xbox 控制器拆分给左右虚拟手柄：左、右摇杆分别控制对应手的摇杆；左右肩键分别控制对应手的扳机；左右模拟扳机分别控制对应手的抓握；View 与 Menu 最终合并到左手系统键；面键按位置映射为 `X/Y → 左手 X/Y`、`A/B → 右手 A/B`。这些映射可在“设置 → XInput”中分别修改，也可以选择“操作手柄以绑定…”后直接按下目标按键；手柄断开时，使用 XInput 的手会立即回到中立状态。

XInput 设置还提供左右手独立的拇指与食指触摸辅助。每项都可选择物理辅助键和“默认接触 / 默认抬起”，按住辅助键时临时反转；若同一个物理键同时承担真实控制器输入，真实输入优先，触摸辅助会忽略该键。

虚拟控制器同时接收游戏发出的 SteamVR 震动事件，并由 Runtime 回传到 XInput 手柄。设置中的“震动映射”可选择保留左右手对应关系、交换左右对应关系，或把当前较强的手部反馈统一发送到两颗马达。手柄断开、路由切换或 Runtime 退出时会立即停止震动。

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

`main` 保留稳定的 v001 历史。v002、v003、v004、v005、v006、v007、v008、v009 已发布；后续开发继续在
`v002-runtime` 分支进行，架构契约见
[`docs/v002-architecture.md`](docs/v002-architecture.md)，驱动开发闭环见
[`docs/driver-development.md`](docs/driver-development.md)。普通构建不会安装或注册驱动。

当前界面以侧边栏管理命名运行时路由，并在“设置”中保留 v001
`TrackingOverrides`、备份和恢复流程。静态映射仍然只负责把稳定虚拟代理
引导到目标，不提供偏移；直接输出和虚拟控制器模式不依赖静态映射。

开发环境启动 Runtime：

```powershell
dotnet run --project src\TrackSwap.Runtime\TrackSwap.Runtime.csproj -c Release -- --run
```

Runtime 默认把原子配置快照保存在
`%LOCALAPPDATA%\TrackSwap\runtime-config.json`。选择“跟随 TrackSwap”时，Runtime
随 UI 启动和退出；选择“跟随 SteamVR”时，驱动会在 SteamVR 会话初始化时拉起 Runtime，
并在会话结束后完成待处理配置再退出。即使 SteamVR 与 Runtime 起初都未运行，UI 也会
临时启动 Runtime 以完成离线配置编辑和静态映射维护。
“TrackSwap 跟随 SteamVR 启停”是独立选项：启用后 SteamVR 会话启动时打开 UI。
SteamVR 完全停止后，没有待处理配置时 UI 会立即关闭；有待删除或待映射内容时，会在处理
完成后关闭。处理失败时保留窗口和待处理状态，避免静默留下未完成配置。

“实时 3D 位姿预览”以约 30 Hz 显示物理来源和最终目标。左侧数值区用于精调，右侧“调整工具”可选择“隐藏”、“移动”或“旋转”；隐藏工具不会清除偏移。按住 Shift 拖动可进行更细微的调整，松开后通过正常应用流程保存。调整工具永远锚定到虚拟输出设备，并使用它的局部坐标轴，即使高级选项隐藏了代理模型，也不会改为跟随来源或目标设备。界面优先通过 OpenVR 加载设备驱动注册的静态渲染模型与纹理，并为
头显、手柄和 Tracker 提供内置回退模型。模型加载和遥测都位于 UI 预览层，
UI 关闭、卡顿或断开不会影响 SteamVR 位姿提交。详见
[`docs/pose-preview.md`](docs/pose-preview.md)。

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
.\scripts\Build-Release.ps1 -Version v009 -Clean
```

安装 Inno Setup 6 后，生成发布包、Windows 安装程序及其 SHA-256 文件：

```powershell
winget install --id JRSoftware.InnoSetup --exact
.\scripts\Build-Installer.ps1 -Version v009 -Clean
```

安装程序采用按用户安装，默认目录为 `%LOCALAPPDATA%\Programs\TrackSwap`。安装与卸载都要求
SteamVR 完全退出。完整卸载会移除 TrackSwap 驱动、SteamVR 应用清单、虚拟代理静态映射、
TrackSwap 创建的 SteamVR 配置备份、用户配置、快捷方式和安装目录。

升级到不同目录中的完整包时，应先完全退出 SteamVR，再运行
`scripts\Install-Driver.ps1 -ReplaceExisting`；脚本注册失败时会尝试恢复旧路径。
卸载驱动运行 `scripts\Uninstall-Driver.ps1`。这些脚本只更改 OpenVR 驱动注册，
不会静默删除用户在 `%LOCALAPPDATA%\TrackSwap` 中保存的配置和校准档案。

生成的程序位于：

```text
src\TrackSwap\bin\Release\net48\TrackSwap.exe
```

## 版本规则

TrackSwap 使用连续编号：`v001`、`v002`、`v003`、`v004`、`v005`、`v006`、`v007`、`v008`、`v009`……不区分主版本、次版本或预发布状态。

推送与项目版本一致的 `vNNN` Git 标签后，GitHub Actions 会自动构建 Windows x64 Release、生成 ZIP 压缩包和 SHA-256 校验文件，并发布对应的 GitHub Release。

## 许可证与声明

TrackSwap 由 Hrenact 以 [MIT License](LICENSE) 开源。

第三方组件及其许可证请参阅 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。TrackSwap 是非官方项目，与 Valve Corporation 或 SteamVR 没有隶属、授权或认可关系。
