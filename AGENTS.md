# TrackSwap VR 仓库指南

本文件适用于整个仓库，除非更深层目录提供了更具体的 `AGENTS.md`。它只保留跨模块基线和文档路由；实施细节按职责拆在 `docs/agent-guides/` 中。

## 开始工作前

先判断改动涉及哪些领域，并完整阅读对应文档。一次改动跨越多个领域时，相关文档都必须读。

| 工作范围 | 必读文档 |
| --- | --- |
| Runtime、路由、代理设备、位姿数学、虚拟 HMD、原生驱动、`TrackingOverrides` | [`docs/agent-guides/runtime-routing.md`](docs/agent-guides/runtime-routing.md) |
| WPF 界面、设置页、弹窗、3D 预览、设备选择器、图标与视觉样式 | [`docs/agent-guides/ui-guidelines.md`](docs/agent-guides/ui-guidelines.md) |
| OSC、XInput、虚拟控制器、触觉反馈、骨骼 | [`docs/agent-guides/controller-input.md`](docs/agent-guides/controller-input.md) |
| 本地化、语言 JSON、翻译键、Crowdin | [`docs/localization.md`](docs/localization.md) |
| 安装、卸载、Steam depot、SteamVR 配置文件、发布 | [`docs/agent-guides/distribution.md`](docs/agent-guides/distribution.md)；发布时另读 [`docs/steam-release-checklist.md`](docs/steam-release-checklist.md) |
| 构建、自动化测试、硬件验证、验收基线 | [`docs/agent-guides/verification.md`](docs/agent-guides/verification.md) |

专题文档是规范，不是背景材料。修改对应模块时不得只读本文件。

## 产品基线

TrackSwap 保留彼此明确分离的工作流：

1. **旧版静态映射**
   - v001 兼容的 WPF 流程编辑 SteamVR `TrackingOverrides`。
   - 它只替换目标设备的位姿来源，并保留目标设备原有输入。
   - 静态 `TrackingOverrides` 不支持通用位置或旋转偏移，任何界面和文档都不得暗示它支持。

2. **Runtime 位姿路由**
   - 独立 Runtime 与原生 OpenVR 驱动负责实时来源切换、局部刚体偏移、遥测、自动重连、隐藏与一致性处理。
   - 当前界面使用配置侧栏；每条路由拥有自己的来源、逻辑槽位、目标、偏移、状态和常驻 3D 预览。

3. **直接虚拟设备输出**
   - 路由可以输出稳定的 TrackSwap VR 虚拟追踪器，不创建 `TrackingOverrides`。
   - 路由也可以输出固定左右虚拟控制器，使用明确的 `无`、OSC 或 XInput 控制输入。
   - 至多一条路由可以输出 `TRKSWAP-HMD`。虚拟 HMD、显示重定向及 VR 视图的硬约束见 Runtime 专题文档。

4. **目标替换**
   - 需要保留目标控制器输入时，使用单独的内部代理设备作为静态 `TrackingOverrides` 来源。
   - 物理来源和偏移是实时 Runtime 状态；不得因热更新而反复改写 `steamvr.vrsettings`。

## 组件职责

### TrackSwap VR UI

- Windows WPF 控制面，目标为 .NET Framework 4.8。
- 只负责编辑、调整、检查、测试、提交配置和显示降采样遥测。
- 不得拥有路由、输入、映射协调或设备输出所需的后台服务，也不得进入逐帧追踪路径。
- 除非提前向用户明确披露不可避免的例外，所有已配置功能在 UI 关闭后仍必须由 Runtime 正常运行。
- 在 Settings 中保留旧版静态设置流程，但与 Runtime 路由清晰隔离。

### TrackSwap VR Runtime

- 是独立的配置与控制进程，拥有持久配置、IPC、验证、来源发现、协调、重试和遥测分发。
- 拥有停止 SteamVR 后的 `TrackingOverrides` 协调、待删除完成、孤立代理清理、OSC/XInput 服务与触觉转发；这些行为不得依赖 UI 存活。
- 对所有虚拟槽位发送带版本号的原子快照，并能在 UI 断开或重连时稳定恢复。
- 支持“跟随 TrackSwap”和“跟随 SteamVR”两种生命周期；存在待协调工作时不得提前退出。

### TrackSwap OpenVR Driver

- 是最小化原生 SteamVR Server Driver，暴露稳定虚拟追踪器、内部代理、固定左右控制器和可选虚拟 HMD。
- 读取物理设备原始位姿、应用变换并提交虚拟位姿；Runtime/UI 断开时保留最后一个有效配置。
- 逐帧路径必须本地、确定、少分配且非阻塞。禁止逐帧同步 IPC、网络操作或进程启动。
- 驱动初始化可在逐帧路径之外请求一次包内可信 Runtime；Runtime 缺失不得使驱动初始化失败。

## 分支与发布

- `v001` 至 `v013` 是已发布且不可变的标签，禁止移动或重写。
- `old-v001` 保留迁移前的稳定 v001 分支历史，禁止删除、重定向或重写。
- `main` 是 v013 之后的当前集成与开发分支；实验性工作应先放在独立主题分支，经验证后再合入。
- 下一发布编号为 `v014`。发布只使用递增的 `v001`、`v002`……序列；除非维护者改变策略，不引入语义版本或预发布后缀。
- 提交保持聚焦，不重写已发布历史。
- 项目使用 Hrenact 名义下的 MIT 许可证。新增第三方代码或二进制依赖必须记录到 `THIRD-PARTY-NOTICES.md` 并保留其许可声明。
- 每次发布前必须重新审计第三方代码、二进制、素材、参考实现及其精确版本或修订，确认声明不存在遗漏或过期项。发现任何问题时必须停止发布，并向用户逐项说明需要新增或更新的内容；不得以“稍后补充”继续构建、打标签或上传发布资产。

## 全局工程约束

- 配置修订和设备快照必须原子化；旧修订不得覆盖新修订，不同快照族不得互相清除仍有效的所有权状态。
- 所有 IPC 边界都要验证标识符、槽位、消息长度、变换和值；无效或断开的来源不得以健康的陈旧位姿继续输出。
- 不得在 SteamVR 运行时写入 `steamvr.vrsettings` 或修改需要重启的虚拟 HMD 注册设置。
- 使用 OpenVR 返回的真实完整设备路径；不得根据序列号猜测 `/devices/lighthouse/` 等前缀。
- 防止自映射、角色自引用、循环、重复槽位、重复手别和冲突目标。允许多条路由复用同一个物理位姿来源，但不得因此放宽其他验证。
- 目标设备输入和位姿来源必须分离。TrackSwap 替换位姿时，不得意外接管或丢失目标输入。
- 优先使用官方 OpenVR 头文件、文档和示例。引入或固定依赖时记录准确修订。
- 状态转换和可操作错误写入英文日志；默认不记录高频位姿，也不把底层日志纳入本地化。
- 生成物、机器路径、测试截图、用户配置、Crowdin 密钥与访问令牌不得进入 Git。
- 修改应按风险成比例验证：构建受影响项目、运行相关自动化测试，并明确说明仍需维护者完成的 SteamVR 或硬件检查。
- 编译成功不等于追踪正确，XAML 构建成功也不等于实际主题可读。

## 进程与测试授权

- 为 TrackSwap 开发和验证，维护者明确授权代理在任务确有需要时停止、启动或重启 SteamVR。操作前仍须核对 `vrserver`、`vrmonitor`、`vrcompositor` 等准确进程，避免影响无关应用，并在进度更新中说明生命周期操作。
- 当 TrackSwap VR UI 或 Runtime 锁住 Release 输出时，可无需再次询问而关闭这些 TrackSwap VR 自有进程。优先优雅退出，只终止准确的残留进程，并报告关闭了什么。
- 需要硬件验证时，在清晰边界停下，给出准确的准备步骤、预期结果和恢复方法；绝不以构建成功代替硬件结论。
