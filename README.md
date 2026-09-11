# PS Mobile Wallpaper

Photoshop UXP 插件 + Windows PhoneBridge，实现「读取 PS 画布 → 自动识别手机 → 按手机屏幕尺寸居中裁剪 → USB 传输 → 设为锁屏壁纸」。

## 架构

```
Photoshop UXP Plugin (TypeScript)
        │  REST + WebSocket  127.0.0.1:18765
        ▼
PhoneBridge (C# / .NET)
        │  IDeviceTransport
        ├── AdbTransport  ──►  Android
        └── HdcTransport  ──►  HarmonyOS
```

## 仓库结构

```
PS-Mobile-Wallpaper/
├── apps/
│   ├── photoshop-plugin/     # UXP 面板插件
│   └── phone-bridge/
│       └── src/
│           ├── PSMobileWallpaper.Api/            # Minimal API + WebSocket
│           ├── PSMobileWallpaper.Application/    # 壁纸工作流编排
│           ├── PSMobileWallpaper.Domain/         # 统一模型与错误码
│           ├── PSMobileWallpaper.Infrastructure/ # 日志、配置、认证
│           ├── PSMobileWallpaper.Device/         # 设备发现与品牌适配
│           ├── PSMobileWallpaper.Transport/      # IDeviceTransport / ADB / HDC
│           ├── PSMobileWallpaper.Image/          # Center Crop 与图片编码
│           └── PSMobileWallpaper.Wallpaper/      # 壁纸 Provider 体系
├── packages/       # 预留：protocol / common / device-model
├── runtime/        # 预留：随包分发的 adb / hdc
├── tests/
│   ├── unit/       # xUnit 单元测试
│   └── integration/
├── installer/wix/  # 预留：WiX Toolset 4
├── docs/
├── scripts/
└── .github/workflows/
```

## 构建与测试

```bash
# PhoneBridge
dotnet build apps/phone-bridge/PSMobileWallpaper.sln
dotnet test  tests/unit/PSMobileWallpaper.Tests/PSMobileWallpaper.Tests.csproj

# 运行 Bridge
dotnet run --project apps/phone-bridge/src/PSMobileWallpaper.Api

# Photoshop 插件
cd apps/photoshop-plugin && npm install && npm run build
```

## 安装

发布产物在 `artifacts/`（由 `scripts/build-installer.ps1` 生成）：

| 文件 | 说明 |
|---|---|
| `PSMobileWallpaper-PhoneBridge-<ver>-x64.msi` | 安装程序。装到 `%ProgramFiles%\PS Mobile Wallpaper\`，含开始菜单与桌面快捷方式 |
| `PSMobileWallpaper-PhoneBridge-<ver>-x64.zip` | 免安装版，解压即用 |
| `PSMobileWallpaper-Plugin-<ver>.zip` | Photoshop UXP 面板 |

可执行文件是**自包含**的：目标机器**不需要 .NET 运行时，也不需要 Adobe 账号**。

```powershell
# 构建全部产物
powershell -ExecutionPolicy Bypass -File scripts/build-installer.ps1 -Version 1.0.0
```

### 为什么不做成 Windows 服务

Bridge 的配置与认证令牌按 §2.7 / §23 存放在**用户**的 `%AppData%\PSMobileWallpaper\`。
以 `LocalSystem` 运行的服务看不到这个目录，插件申请的令牌会失效。因此安装包只创建快捷方式，
开机自启由用户自行添加（把快捷方式放进 `shell:startup`）。

## 配置

首次运行会在 `%AppData%\PSMobileWallpaper\` 生成：

| 文件 | 用途 |
|---|---|
| `config.json` | 服务端口、adb/hdc 路径、图片格式与质量、壁纸模式 |
| `auth.token` | 本地认证令牌，插件请求时需携带 |
| `logs/psmw-*.log` | 按天滚动的运行日志 |

`config.json` 默认内容：

```json
{
  "server": { "host": "127.0.0.1", "port": 18765 },
  "adb": { "enabled": true, "path": "" },
  "hdc": { "enabled": true, "path": "" },
  "image": { "format": "png", "cropMode": "center-crop", "quality": 95 },
  "wallpaper": { "saveToGallery": true, "setLock": true }
}
```

`adb.path` / `hdc.path` 留空表示从 `PATH` 查找；也可填可执行文件或其所在目录的绝对路径。

## API（`/api/v1`）

| 方法 | 路径 |
|---|---|
| GET | `/api/v1/devices` |
| GET | `/api/v1/devices/{deviceId}` |
| GET | `/api/v1/devices/{deviceId}/display` |
| GET | `/api/v1/devices/{deviceId}/capabilities` |
| POST | `/api/v1/images` |
| POST | `/api/v1/images/crop` |
| POST | `/api/v1/wallpaper/prepare` |
| POST | `/api/v1/wallpaper/send` |
| POST | `/api/v1/wallpaper/set-lock` |
| POST | `/api/v1/wallpaper/set-home` |
| POST | `/api/v1/wallpaper/set-both` |
| WS | `/ws`（`device.*` / `transfer.*` / `wallpaper.*` 事件） |

除 `/health` 外，所有请求需带 `X-PSMW-Token` 头，或 WebSocket 用 `?token=` 查询参数。

## 当前进度

**Phase 1（文档 §41 全部 14 项）— 已完成**

- 项目结构与 CI
- UXP 插件工程（§31 面板 UI、§32 状态、§33 进度）
- PhoneBridge 八个项目，可构建、可运行
- Domain Models（§28 / §29 / §30）
- `IDeviceTransport` + `AdbTransport` / `HdcTransport`（§13 / §15 / §16）
- `DeviceManager` + 品牌 `IDeviceAdapter` 体系（§5 / §25 / §26）
- REST API（§21）+ WebSocket（§22）
- Serilog 日志（§2.6）+ JSON 配置（§2.7 / §24）
- xUnit 单元测试（§2.10）

**Phase 3（ADB）— 已在真机验证**

用 Huawei JAD-AL80（Android 12）实测通过：设备检测、设备发现、状态映射、设备信息
（`getprop`）、屏幕尺寸（`wm size` / `wm density`）、Shell、Push、Pull 往返。

**Phase 4（HDC）— 已在真机验证**

用 HarmonyOS 设备（HUAWEI EMA-AL00U，OpenHarmony 7.0.0.105，1280×2800）实测通过：
`hdc` 检测、设备发现、状态映射、`param get` 设备信息、`hidumper` 屏幕尺寸与密度、
Shell、`file send` / `file recv` 往返。集成测试 14/14 全绿，无跳过。

**Phase 5（UXP ↔ Bridge）— 契约已验证**

`scripts/phase5-e2e-check.py` 对运行中的 Bridge 做端到端验证，全部通过：

1. WebSocket 无 token 握手 → 401（§23）
2. WebSocket 带 token 握手 → 101
3. 真机识别 → Huawei JAD-AL80，1228×2700
4. `POST /wallpaper/prepare` → 将源图裁剪为 **1228×2700**（§10）
5. 同时收到 WebSocket 事件 `{"event":"device.connected","data":{...}}`（§22）

另有一组契约测试（`tests/unit/.../Contracts/WireContractTests.cs`）锁定线上 JSON 形状，
确保 §5.4 / §6 / §19 / §22 / §29 / §30 定义的字段集不被计算属性污染。

**Phase 7（图片处理）— §11 全部模式已实现并验证**

| 模式 | 说明 |
|---|---|
| `center-crop` | 居中裁剪，铺满目标（默认） |
| `center-fit` | 完整适应，保留整图，留边补黑/透明 |
| `stretch` | 拉伸铺满（不保持比例） |
| `top-crop` | 同 center-crop 的取景尺寸，锚定顶部 |
| `bottom-crop` | 同上，锚定底部 |
| `custom` | 用户指定源区域（**仅 API**，插件 UI 未提供区域编辑器） |

`scripts/crop-modes-check.py` 用纯 Python 解码 Bridge 输出的 PNG 并断言真实像素：
center-fit 必须留边、其余模式必须铺满、三种纵向锚定的取样窗口必须依次下移。
在真机上实测得到首行 green 依次为 `0 / 34 / 68`，与几何计算完全吻合。

注意：当源图**宽于**目标时，裁剪会保留全高、只裁左右，此时 `center-crop`、`top-crop`、
`bottom-crop` 在数学上等价 —— 纵向锚定只有在源图相对更高时才有区别。

**尚未完成**

- Phase 6：插件侧画布导出尚未进 Photoshop 实机验证（本机缺 UXP Developer Tool）
- Phase 9 / 10：**真实的锁屏壁纸设置尚未按品牌验证**

## 验证方式

```bash
# 单元测试（含线上格式契约与全部裁剪模式几何）
dotnet test tests/unit/PSMobileWallpaper.Tests/PSMobileWallpaper.Tests.csproj

# 集成测试：需要真机；无设备时自动跳过并保持绿色
dotnet test tests/integration/PSMobileWallpaper.IntegrationTests/PSMobileWallpaper.IntegrationTests.csproj

# 端到端（需先启动 Bridge）
python scripts/phase5-e2e-check.py    # 设备识别 + WS 事件 + 裁剪
python scripts/crop-modes-check.py    # §11 全部裁剪模式的像素级验证
```

## 重要说明

### 关于壁纸能力的保守声明

文档 §40 明确要求：**不得在没有验证的情况下声称某个手机品牌支持自动设置锁屏**。

因此当前所有 `IWallpaperProvider`：

- `CanSetLock` / `CanSetHome` / `CanSetBoth` 一律为 `false`
- `CanSaveToGallery` 为 `true`（推送文件 + 媒体扫描，无需厂商适配）
- 对未验证设备返回错误码 `WALLPAPER_NOT_SUPPORTED`

厂商壁纸能力需在 Phase 10 用真机逐品牌验证后，才在对应 Provider 中放开。

### 与文档的技术栈差异

| 项 | 文档要求 | 实际 | 原因 |
|---|---|---|---|
| .NET 版本 | .NET 8 | **net10.0** | 本机仅装 .NET 10 SDK / 运行时；经确认后改用 net10.0。其余技术栈（ASP.NET Core Minimal API、Serilog、SkiaSharp、xUnit）与文档一致 |
| 解决方案文件 | — | 经典 `.sln` | .NET 10 SDK 默认生成 `.slnx`；本机未装 Visual Studio，`.sln` 兼容性更好 |

### 未使用的能力

仓库内**不包含也不调用**任何厂商壁纸破解方案（如 `HWthemeCrack.bat`）。文档 §42 禁止绕过设备安全机制、Root、修改手机系统。
