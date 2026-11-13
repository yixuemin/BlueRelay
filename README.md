# BlueRelay（蓝桥音频）

把 Windows 电脑变成蓝牙音箱：接收手机（A2DP 音频源）的蓝牙音频并在电脑上播放，同时提供**独立音量控制**和**输出设备快捷切换**。常驻系统托盘，点开即用。

## 功能特性

- **蓝牙 A2DP 接收**：通过 Windows 原生 `AudioPlaybackConnection` API 接收手机音频，无需虚拟声卡。
- **独立音量控制**：单独调节"手机→电脑"这一路音频的音量，与系统主音量互不影响。
- **输出设备快捷切换**：在面板里一键切换播放设备（扬声器/耳机等），切换后自动重连音频会话。
- **连接加载动画**：连接/重连过程中按钮显示旋转指示器。
- **深色 / 浅色主题**：运行时切换，持久化保存。
- **设置弹窗**：深色模式、开机自启、自动重连、低端硬件模式（降低 SBC bitpool 以稳定弱信号）。
- **自动修复无声**：连接后每秒检测默认输出音频峰值，10 秒完全无声自动断开重连一次（可关闭）。
- **仅监听模式**：只等待、由手机发起连接，兼容「显示已连接但不推流」的机型（可关闭）。
- **指数退避自动重连**：2s 起步翻倍、±20% 抖动、20 次熔断，避免风暴式重连。
- **音频稳定模式**：连接期间在专用线程启用 MMCSS "Pro Audio" 调度，减少卡顿（引用计数、空闲零 CPU）。
- **虚拟声卡警告**：默认输出命中 FxSound / VB-Cable / Voicemeeter 等虚拟声卡时面板显示醒目警告。
- **单音源约束**：连接新设备前自动断开其他连接，同一时刻只播放一台（蓝牙协议限制）。
- **协议前缀**：设备列表显示 `[A2DP]` / `[LE Audio]` 前缀。
- **测试声音**：一键播放系统提示音，确认电脑扬声器/当前输出设备正常。
- **单实例保护**：GUI 单实例，二次启动激活已有面板；托盘支持连接上次设备、断开当前连接、打开蓝牙设置。
- **命令行接口（CLI）**：`--list` / `--connect` / `--disconnect` / `--toggle`。
- **崩溃报告**：本地日志，不上传遥测。

> 轻量设计：托盘空闲时不创建主窗口，实测私有内存约 20 MB、CPU 0%。

## 技术栈

| 组件 | 用途 |
|---|---|
| .NET 8 + WPF | UI 框架 |
| CommunityToolkit.Mvvm | MVVM（ObservableObject / RelayCommand） |
| NAudio | CoreAudio 枚举 / 会话音量 |
| Windows.Media.Audio (WinRT) | 蓝牙 A2DP 接收 |
| Microsoft.Extensions.DependencyInjection | 依赖注入 |

## 项目结构

```
BlueRelay/
├── Models/            数据模型（AppSettings / AudioDeviceInfo / BluetoothDeviceInfo）
├── Services/          业务逻辑（音频、蓝牙、设置、CLI、崩溃报告、低端模式、Win32 互操作）
├── ViewModels/        MVVM 视图模型（MainViewModel / SettingsViewModel）
├── Views/             WPF 窗口（MainWindow / SettingsWindow）
├── Themes/            深浅主题资源字典（Dark.xaml / Light.xaml）
├── App.xaml(.cs)      应用入口：DI 容器、托盘、主题、CLI 路由
└── BlueRelay.csproj
```

架构遵循严格 MVVM：Views 仅保留窗口生命周期（圆角、Esc 关闭），业务逻辑全部在 Services / ViewModels 中，无逻辑散落在 code-behind。

## 构建

要求：Windows 10 2004 (build 19041) 或更高（Win11 推荐），已安装 .NET 8 SDK。

```bash
dotnet restore
dotnet build -c Release
```

产物位于 `bin/Release/net8.0-windows10.0.22621.0/BlueRelay.exe`。

## 使用

1. 先在系统蓝牙设置里把手机与电脑配对。
2. 运行 `BlueRelay.exe`，点托盘图标打开面板。
3. 点「连接」，在手机上播放音频，声音从电脑播放。
4. 在面板里拖动音量滑块调节"手机→电脑"这一路音量，或切换输出设备。

## 开发与自测

- `--show`：启动后立即打开面板（预览）。
- `--uitest`：自动交互测试，驱动「显示→采样动画→调音量→连接→切设备→开设置弹窗」并逐帧截图到 `uitest/`，结束后自动恢复初始状态并退出。

## 致谢 Acknowledgements

本项目参考了以下开源项目，特此致谢：

- **[ysc3839/AudioPlaybackConnector](https://github.com/ysc3839/AudioPlaybackConnector)**（MIT）—— 蓝牙 A2DP 接收的原始实现，`AudioPlaybackConnection` 的调用方式（`TryCreateFromId → StartAsync → OpenAsync`）参考于此。
- **[GorangN/EasyBluetoothAudio](https://github.com/GorangN/EasyBluetoothAudio)**（MIT）—— 架构（严格 MVVM + DI）、连接状态模型（Connecting/Connected 等枚举与 `IsBusy`）、设置界面分区布局参考于此。
- **[Belphemur/SoundSwitch](https://github.com/Belphemur/SoundSwitch)**（GPL-3.0）—— 输出设备切换的 CoreAudio 思路（`IPolicyConfig.SetDefaultEndpoint`）。仅参考思路与 API 用法，未复制其代码，本项目保持 MIT。
- **[naudio/NAudio](https://github.com/naudio/NAudio)**（MIT）—— CoreAudio 设备枚举与会话音量的底层封装。
- **[CommunityToolkit/dotnet](https://github.com/CommunityToolkit/dotnet)**（MIT）—— MVVM 源生成器。

## 许可证

[MIT](LICENSE)
