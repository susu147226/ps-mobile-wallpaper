# PS Mobile Wallpaper v1.0.0

把 Photoshop 画布变成手机壁纸：识别手机 → 读取画布 → 按屏幕尺寸裁剪 → USB 传输 → 设为壁纸。

## 下载

| 文件 | 用途 |
|---|---|
| `PSMobileWallpaper-PhoneBridge-1.0.0-x64.msi` | **推荐**。安装 PhoneBridge 到 `%ProgramFiles%`，含开始菜单与桌面快捷方式 |
| `PSMobileWallpaper-PhoneBridge-1.0.0-x64.zip` | 免安装版，解压后直接运行 `PSMobileWallpaper.Api.exe` |
| `PSMobileWallpaper-Plugin-1.0.0.zip` | Photoshop UXP 面板（也可在安装目录的 `plugin\` 下找到） |

可执行文件是自包含的：**无需安装 .NET 运行时，也无需 Adobe 账号**。

需要 64 位 Windows。

## 快速开始

1. 安装 PhoneBridge 并启动（开始菜单 → PS Mobile Wallpaper PhoneBridge）
2. 首次运行会生成：
   - `%AppData%\PSMobileWallpaper\config.json`
   - `%AppData%\PSMobileWallpaper\auth.token`
3. 用 USB 连接手机，在手机上授权 USB 调试
4. 在 Photoshop 中加载面板（`PSMobileWallpaper-Plugin-1.0.0.zip` 或安装目录下的 `plugin\`）
5. 把 `auth.token` 的内容粘贴到面板的 Token 输入框
6. 打开要作为壁纸的文档 → 预览裁剪结果 → 传输到手机 → 设置为锁屏壁纸

Bridge 只监听 `http://127.0.0.1:18765`，且除 `/health` 外的所有请求都必须携带 Token。

## 已实现

**设备通信**

- ADB（Android）与 HDC（HarmonyOS）双通道，统一 `IDeviceTransport` 抽象
- 设备自动发现、状态映射、品牌/型号/系统识别、屏幕尺寸与密度读取
- 已在真机验证：Huawei JAD-AL80（Android 12，1228×2700）、
  HUAWEI EMA-AL00U（HarmonyOS 7.0.0.105，1280×2800）

**图片处理**

- `center-crop`（默认）、`center-fit`、`stretch`、`top-crop`、`bottom-crop`、`custom`
- PNG / JPEG 输出，写入 `%TEMP%\PSMobileWallpaper\`

**接口**

- REST API `/api/v1`（设备、图片、壁纸）
- WebSocket `/ws`（`device.*` / `transfer.*` / `wallpaper.*` 事件）
- 本机认证 Token，仅监听回环地址

**Photoshop 面板**

- 设备选择、状态指示、屏幕/画布尺寸、裁剪方式、输出格式
- 传输进度显示
- 原生日志写入 `%AppData%\PSMobileWallpaper\logs\`

## 已知限制

- **锁屏壁纸自动设置尚未实现**。设备侦察表明：Android 的 `cmd wallpaper` 在这些机型上无实现，
  shell 虽持有 `SET_WALLPAPER` 权限但没有命令行出口；HarmonyOS 的 `WallpaperManagerService`
  经 hidumper 不暴露任何接口。目前所有 Provider 对锁屏/主屏设置返回
  `WALLPAPER_NOT_SUPPORTED`，**保存到相册是唯一已实装并验证的壁纸能力**（spec §40 要求不得
  在未验证时声称支持）。
- 插件尚未在 Photoshop 中实际加载验证（需要 UXP Developer Tool）。
- 插件 UI 未提供 `custom` 裁剪的区域编辑器，该模式目前仅可通过 API 使用。

## 验证

- 单元测试 132 项（含全部裁剪模式几何与线上 JSON 格式契约）
- 集成测试 14 项，针对两台真机，无跳过
- 端到端脚本：设备识别、WebSocket 事件信封、裁剪模式像素级断言
- 安装包实测：静默安装 → 运行（识别两台真机）→ 干净卸载

## 技术说明

- 目标框架 `net10.0`（文档原文要求 .NET 8；本机仅有 .NET 10 SDK，已记录此偏离）
- 安装包用 WiX Toolset 4 构建
- 不包含、也不调用任何厂商壁纸破解方案；不 root、不修改手机系统（spec §42）
