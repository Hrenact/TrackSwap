# TrackSwap VR Steam 发布清单

这份清单用于 v013 的 Steam 上架准备。它不包含 Steamworks 凭据、AppID、DepotID 或未公开商店资料。

## Depot 与启动项

- 使用 `scripts/Build-SteamDepot.ps1 -Version v013 -AppId <AppID> -DepotId <DepotID> -Clean` 生成 SteamPipe 内容与预览配置。
- 脚本默认写入 `"Preview" "1"`，先检查 `artifacts/steam/v013/output` 的文件清单，再显式移除预览标记并上传。
- Steamworks 的默认 Windows 启动项指向 `TrackSwap.exe`，启动目录使用 depot 安装根目录。
- `UserData` 必须始终排除在 depot 之外。它由程序运行时创建，更新、验证和普通卸载不会管理它。
- `installscript.vdf` 在首次运行时注册 OpenVR 驱动与 TrackSwap VR 应用清单；卸载时移除这些集成，但保留 `UserData`。
- 安装、更新后的首次注册以及卸载都要求 SteamVR 完全退出。若安装步骤返回非零，Steam 会在后续启动时重试；发布测试必须覆盖“SteamVR 正在运行”的提示与恢复路径。

## Steamworks 页面

- 将产品类型、系统要求、支持语言、VR 依赖与实际程序行为保持一致。
- 明确说明 TrackSwap VR 是第三方 SteamVR 工具，不隶属于 Valve、Meta、Pico、VRChat 或其它设备/软件厂商。
- 商店截图不得包含聊天通知、用户名、设备序列号、物理设备路径或其它个人信息。
- 上传胶囊图、库图、图标和截图后，在 Steam 客户端的 100%、125%、150% 缩放下检查可读性。
- 完成内容调查问卷、隐私信息、第三方许可与开源声明。TrackSwap VR 不收集遥测；诊断包为用户主动导出且经过脱敏。

## 安装与升级测试

- 全新安装：未运行过 SteamVR、已运行过 SteamVR、SteamVR 安装在其它盘。
- 首次启动：SteamVR 已关闭与正在运行两种情况；驱动注册失败时不得留下半注册状态。
- 覆盖升级：保留 `UserData`、路由、OSC/XInput 设置、校准档案与 UI 偏好。
- 旧版迁移：从 `%LOCALAPPDATA%\TrackSwap` 迁移到安装目录 `UserData`，验证后选择保留或清理旧版文件。
- 验证文件完整性：程序文件恢复，`UserData` 不丢失。
- 普通卸载：驱动、manifest、自动启动、TrackSwap VR 静态映射被移除；`UserData` 保留。
- 重装：自动重新使用残留的 `UserData`。
- 手动完整清理：只删除明确属于 TrackSwap VR 的文件，不删除未知文件、SteamVR 日志或其它 OpenVR 配置。

## 功能回归

- 四种运行模式、拆分位置/旋转来源、本地偏移、3D 预览、来源隐藏。
- 虚拟控制器输入、触摸辅助、Aim/Grip 位姿、左右手震动、OSC 收发与端口冲突状态。
- UI 关闭后 Runtime 独立运行；“跟随 SteamVR”与“跟随 TrackSwap VR”两种生命周期。
- SteamVR 停止后的待映射、待删除与孤立代理清理，不依赖 UI。
- 导出配置、覆盖导入、自动回滚、损坏备份拒绝、迁移后重启。

## 发布前门槛

- 逐项审计所有第三方代码、二进制、素材、带再分发义务的工具和参考实现；核对 `THIRD-PARTY-NOTICES.md` 中的项目、版本、固定修订及要求随包附带的原始许可文件。发现遗漏、过期、含糊或许可文件不匹配时，立即停止发布并向用户列出全部待更新项，禁止继续打标签、打包或上传。
- 运行 `scripts/Test-ThirdPartyNotices.ps1`。自动检查通过仅是最低门槛，不能代替上述人工审计。
- 确认自包含 Runtime 中的 `DOTNET-LICENSE.txt` 与 `DOTNET-THIRD-PARTY-NOTICES.txt` 来自本次发布实际解析的 `Microsoft.NETCore.App.Runtime.win-x64` 精确版本，并通过发布脚本的逐字节校验。
- Debug 与 Release 全量构建零警告。
- Protocol、Runtime、手机震动诊断与安装注册测试全部通过。
- SteamPipe Preview 文件清单不包含 `UserData`、PDB、临时文件、诊断包或开发者本机路径。
- 在干净 Windows 用户账户进行一次 Steam 安装、启动、SteamVR 会话、卸载、重装闭环。
- 完成人工 UI 检查：普通、悬停、选中、禁用、错误、弹窗、下拉列表、工具提示与高 DPI。

SteamPipe 的 depot 文件映射、`InstallScript` 标记和预览构建遵循 Steamworks 官方文档：

- <https://partner.steamgames.com/doc/sdk/uploading>
- <https://partner.steamgames.com/doc/sdk/installscripts>
