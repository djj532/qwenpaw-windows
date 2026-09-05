# QwenPaw Windows Native Client (.exe)

基于微软官方 WebView2 打造的轻量级 Windows 专属客户端。

## 特性
- **彻底解决切后台/最小化断流**：内置 `--disable-background-timer-throttling` 等参数，消除 Chromium 睡眠标签机制；
- **免密直连**：内置客户端专用安全通信指纹，直连 `https://paw.xdjj.asia/`；
- **原生极速**：基于 Windows 10/11 内置的 WebView2 运行时，单文件便携免安装；
- **前台自愈**：窗口被激活时自动对齐会话数据，不丢草稿。
