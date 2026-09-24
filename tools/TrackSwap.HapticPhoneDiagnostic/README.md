# TrackSwap 手机振动诊断器

这是一个不会打进正式安装包的开发诊断工具。它监听 TrackSwap Runtime 真正发送的 OSC/UDP 触觉包，通过 WebSocket 转发给同一局域网内的手机网页，再调用浏览器的振动 API。

## 使用

1. 启动诊断器：

   ```powershell
   dotnet run --project tools\TrackSwap.HapticPhoneDiagnostic\TrackSwap.HapticPhoneDiagnostic.csproj -c Release
   ```

2. 在 TrackSwap 的 OSC 设置中使用地址 `127.0.0.1`，发送端口使用 `9016`。
3. 若 Windows 防火墙询问，只允许“专用网络”。
4. 用同一局域网内的 Android 手机扫描桌面页面显示的二维码。
5. 在手机上点击“启用手机振动”，再点击 TrackSwap 左手或右手体感卡片的“测试”。
6. 按 `Ctrl+C` 停止诊断器。

默认监听 OSC UDP `9016`，网页使用 HTTP `9017`。可选参数：

```text
--udp-port <端口>
--http-port <端口>
--host-address <IPv4>
--no-open
```

如果电脑存在多个网卡而二维码地址选错，可用 `--host-address 192.168.x.x` 明确指定手机能够访问的地址。

## 边界

- 手机网页不能直接监听 UDP，所以本工具必须在电脑上运行桥接服务。
- 网页 API 只能表达振动和暂停的时长。本工具用脉冲密度近似频率、占空比近似强度，不能验证手机电机的真实频率或振幅。
- `navigator.vibrate()` 返回成功只代表浏览器接受了请求；静音、勿扰模式或系统设置仍可能阻止硬件振动。
- iPhone 和 iPad 浏览器目前通常不提供 Vibration API，建议使用 Android Chrome 或 Firefox。
- 每次启动都会生成新的随机访问令牌；停止进程后服务和令牌同时失效。
