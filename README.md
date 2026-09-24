# QwenPaw Windows Native Client (.exe)

基于微软官方 WebView2 打造的轻量级 Windows 专属原生客户端。

## v1.2.0 新特性
- **本地 WebUI 极速加载**：内置静态资源包，通过 WebView2 虚拟域名映射（`appassets.local`）实现毫秒级首屏极速渲染；
- **启动直达上次对话**：自动记忆上次对话 ID（`last_chat_id`），冷启动直达上次会话，SPA 路由切换无整页刷新；
- **原生对话恢复视图**：提供沉浸式原生等待视图与状态反馈，消灭首启白屏与卡顿，加载完成平滑淡出；
- **透明同源 API 代理**：基于 C# HttpClient 实现透明 API 请求转发，自动注入鉴权 UA 与 Cookie，免除 CORS 限制；
- **双模探测与容灾自愈**：局域网直连 / 公网漫游智能探测与故障无感切入，本地静态包异常自动降级云端远程；
- **后台常驻防休眠**：注入 `--disable-background-timer-throttling` 等参数，消除 Chromium 睡眠标签机制，最小化依然全速实时输出。
