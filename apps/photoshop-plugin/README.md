# PS Mobile Wallpaper — Photoshop UXP Plugin

Photoshop 面板插件，把当前画布裁剪成手机屏幕尺寸，经 PhoneBridge 通过 USB 推到手机（spec §2.1、§31）。

## 技术栈

- Adobe UXP（manifest v5）
- TypeScript + Webpack
- 原生 DOM + CSS（**不使用 React**，spec §2.1）

## 目录结构

```
photoshop-plugin/
├── src/
│   ├── api/          # PhoneBridge REST / WebSocket 客户端
│   ├── components/   # 面板 UI 逻辑
│   ├── models/       # 与 Bridge 对齐的共享类型
│   ├── services/     # Photoshop 文档读取与画布导出
│   ├── types/        # 本地 UXP / Photoshop 模块声明
│   ├── utils/        # 格式化工具
│   ├── index.html
│   ├── styles.css
│   └── main.ts
├── manifest.json
├── package.json
├── tsconfig.json
└── webpack.config.js
```

## 构建

```bash
npm install
npm run build      # 产物输出到 dist/
npm run typecheck  # 仅做类型检查
npm run watch      # 开发模式
```

## 加载到 Photoshop

1. `npm run build`
2. 用 **UXP Developer Tool** 加载 `apps/photoshop-plugin/dist` 目录
   （本机尚未安装 UDT，见根 README「已知缺口」）
3. 在 Photoshop 中打开面板：`增效工具 / 插件` → PS Mobile Wallpaper

## 使用

前置：先启动 PhoneBridge（`dotnet run --project apps/phone-bridge/src/PSMobileWallpaper.Api`）。

1. 用 USB 连接手机，确认已开启 USB 调试并在手机上授权本机
2. 面板会自动检测设备；点「获取手机尺寸」拉取屏幕分辨率
3. 打开要作为壁纸的 Photoshop 文档
4. 点「预览裁剪结果」→ 导出画布并居中裁剪到屏幕尺寸
5. 点「传输到手机」推送到手机相册
6. 点「设置为锁屏壁纸」应用

## 本地认证

PhoneBridge 要求请求携带 token（spec §23），内容是 `%AppData%\PSMobileWallpaper\auth.token`。
面板提供两种填入方式：

- 点「从文件读取」，用文件选择器选中上述 `auth.token`
- 或直接打开该文件，把内容粘贴到输入框

UXP 沙箱无法直接访问 `%AppData%`，所以必须由用户手动提供一次。

## 关于 Photoshop 类型声明

Adobe 未在 npm 上发布维护中的 Photoshop UXP typings 包，因此 `src/types/modules.d.ts`
本地声明了本插件实际用到的 API 子集（`app`、`core.executeAsModal`、`Document.saveAs`、
`uxp.storage`）。**新增 Photoshop API 调用时需要同步扩展该文件。**
