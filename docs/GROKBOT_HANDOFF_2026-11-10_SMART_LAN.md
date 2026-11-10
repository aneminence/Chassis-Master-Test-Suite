# Grokbot 交接：Smart LAN 与版本更新（2026-11-10）

## 当前结论
- Windows：CMTS 0.3.2，Android：CMTS 0.1.3（versionCode 4）。
- 用户已在 Note20 手机热点场景实测 Windows 无 token 连接成功。此前脚本自动订阅时断开，不推翻用户手动连接成功；持续实时流自动验收仍待补充。
- 当前范围：Windows 一次连接一台 Android。手机/电脑需要在同一可达局域网；多个手机可由同一路由器接入，但 Windows 多设备并行采集未实现。公网不在本次范围。

## Windows 改动
- `Communication/Remote/CMTSLanDiscovery.cs`：有限网关候选、mDNS/DNS-SD、有界设备探测及协议响应校验。
- `Communication/Remote/CMTSMdnsDiscovery.cs`：有界 DNS-SD 查询；处理 PTR→SRV→A 跨包关联，限制后续查询和超时。
- `CMTSRemoteDataSource`：按 deviceId 校验身份，断线后重新发现，退避重连；旧 authRequired 设备保留高级 token 兼容。
- 连接界面列表只显示设备商品型号和 IP；双击连接；高级手动连接折叠。
- SM-N9860 映射为 Samsung Galaxy Note20 Ultra 5G。
- Windows 180 项单测通过，v3 独立 Debug 构建通过。路径：`D:\CMTS\_build_verify\smart-lan-win-v3\Chassis Master Test Suite.exe`。

## Android 改动
- 使用 Android NsdManager 注册 `_cmts._tcp.`，增加 versioned `device.discover`，返回设备型号和能力信息。
- `device.hello` 不要求 token，但仍验证协议版本；未 hello 的 socket 不能订阅或接收状态/采样广播。
- 设备型号独立于用户自定义 deviceLabel；SM-N9860 显示 Samsung Galaxy Note20 Ultra 5G。QR 不含凭据，仅提供版本化连接元数据。
- 修复未授权 socket 收到状态广播的问题；增加客户端数量、握手长度和未授权空闲时间限制。
- Android `assembleDebug testDebugUnitTest lintDebug` 通过，82 项测试通过。APK：`D:\CMTS\android\app\build\outputs\apk\debug\app-debug.apk`。

## 连接与安全口径
用户明确选择个人局域网免 token 模式，不需手机确认。任何能访问该局域网端口的客户端均可在兼容协议 hello 后读取实时数据并使用远程录制控制。仍为明文 `ws://`，不提供加密或网络内身份认证。不要把发现当作认证。

## 当前验收证据 / 待办
- Note20 热点 IP 曾为 `192.168.202.232`，Windows `192.168.202.118`；用户确认热点设备被发现并无 token 连接成功。
- 手工用户连接成功是本次结论。代理脚本发现/无 token hello曾通过；同脚本订阅阶段异常断开，需调查测试工具或服务时序并补持续样本验收。
- 蜂窝数据关闭场景未确认（Android global setting读取为1）；锁屏2小时、USB GNSS、反复热点切换也未验收。
- 不要声称实时数据持续传输、长期锁屏稳定或公网/多手机并行通过。

## 仓库状态
- 两端工作分支：`feature/smart-lan`。
- Win：`D:\CMTS\Chassis Master Test Suite`，原默认 `main` tip `93ba0d2`。
- Android：`D:\CMTS\android`，原默认 `master` tip `6b2893f`。
- README 已更新；版本 SSOT 已更新到 Win 0.3.2 / Android 0.1.3。
- 不要把交接文档中此前 token/配对流程当成当前决定；本文件是最新产品口径。
- 本交接与改动待提交并推 GitHub；推送前检查 staged diff，仅包含 Smart LAN、版本和 README/交接相关文件。不要推送个人 scratch 或 APK。

## 真机连接操作
1. Note20 开启热点，Windows 加入该热点；手机 CMTS 打开并保持服务运行。
2. Windows 打开 CMTS → Remote/局域网设备 → 搜索；设备列表仅显示商品型号。
3. 双击设备，无 token 输入步骤。手动连接在高级区域。
4. Android 本地采集和 USB GNSS 独立于 Windows 连接；远程断开不应停止本地采集。

## 官方型号依据
三星官方支持页将 SM-N9860 标为 Galaxy Note20 Ultra 5G：
https://www.samsung.com.cn/support/model.SM-N9860ZNGCHC/
