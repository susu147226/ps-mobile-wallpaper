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

> **它不是一个可以浏览的网站。** Bridge 是给 Photoshop 插件调用的本地 API，根路径没有端点，
> 用浏览器直接打开会得到 `PERMISSION_DENIED` —— 那正是 §23 的防护在生效（浏览器不会带 Token）。
>
> 想确认服务是否在跑，访问唯一免 Token 的端点：
>
> ```bash
> curl http://127.0.0.1:18765/health
> # {"status":"ok","version":"1.0.0"}
> ```
>
> 想手动查看设备列表，需要带上 Token：
>
> ```bash
> curl -H "X-PSMW-Token: $(cat "$APPDATA/PSMobileWallpaper/auth.token")" \
>      http://127.0.0.1:18765/api/v1/devices
> ```

真正的人机界面是 Photoshop 面板：把 `auth.token` 的内容粘进面板的 Token 输入框即可。

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

**Phase 9 / 10（Android 壁纸设置）— 真机验证，结论是"只支持主屏"**

用 Huawei JAD-AL80（Android 12 / EMUI）实测：

| 目标 | 结果 |
|---|---|
| 主屏（`FLAG_SYSTEM`） | ✅ **实际生效**（满屏纯色测试，肉眼确认） |
| 锁屏（`FLAG_LOCK`） | ❌ **无效** |

**为什么锁屏无效** —— 这里有两条容易被误导的"假阳性"证据，必须警惕：

1. helper 调用 `setStream(..., FLAG_LOCK)` **返回成功**
2. `dumpsys wallpaper` 里 **Lock 壁纸 id 确实变了**

但锁屏实际**没有任何变化**。EMUI 的锁屏壁纸由华为主题引擎渲染，其 provider 受
`com.huawei.android.thememanager.permission.THEME_PROVIDER_ACCESS` 这个 **signature 级权限**
保护，第三方应用无法获得。

因此 `canSetLock` 被设为 `false`，设置锁屏会返回 `WALLPAPER_NOT_SUPPORTED` 并说明原因。
**判断能力必须以屏幕上的实际效果为准，不能以 API 返回值或 dumpsys 为准**（spec §40）。

实现方式：本仓库附带 helper APK（`apps/wallpaper-helper`），通过官方 WallpaperManager API
代为设置。为什么必须这样做 —— `adb shell` 本身做不到：`cmd wallpaper` 在这些机型上返回
`No shell command implementation`，shell 用户虽持 `SET_WALLPAPER` 权限却无命令行出口。

这不是破解：不 root、不改系统镜像、不绕过安全检查（spec §42）。

## HarmonyOS 壁纸：已实测判定为不可行

在 EMA-AL00U（OpenHarmony 7.0.0.105 / API 26）上做了完整的可行性验证，**用一个签名可安装的
诊断 HAP 逐层排除**（不是靠推断）。

| 环节 | 结论 | 依据 |
|---|---|---|
| 构建 + **签名** HAP | ✅ 可行 | 需 DevEco 登录华为账号自动生成签名；`SignHap` 成功，`install bundle successfully` |
| 安装到设备 | ✅ 可行 | 签名正确即可安装（SDK 自带的 OpenHarmony 证书不行，会 `verify certificate chain failed`） |
| **`SET_WALLPAPER` 权限** | ✅ **可获取** | `checkAccessToken -> 0`、`requestPermissionsFromUser -> [0]`，普通签名应用即可 |
| `GET_WALLPAPER` 权限 | ❌ 系统级 | 声明后**安装直接失败**：`grant request permissions failed` |
| 读取图片文件 | ✅ 可行 | 把资源图写进应用自己沙箱后 `source file readable` |
| **`setWallpaper` 实际生效** | ❌ **不生效** | API 返回成功，但**锁屏和主屏都没有任何变化** |

**决定性结论**：`@ohos.wallpaper` 的 `setWallpaper` 自 API 9 起标注废弃，在 API 26 上是一个
**"报成功但不做事"的存根**。两个调用形式都要单独测：

- `await setWallpaper(...)`（Promise 形式）—— **永远不 resolve，也不 reject**
- `setWallpaper(..., callback)`（回调形式）—— 回调返回**成功**，但屏幕不变

**这不是权限或路径问题**：权限已授予、文件可读、API 报成功，而屏幕纹丝不动。

### 平台的正规路径也走不通

进一步查到，系统自己的壁纸能力由 `com.ohos.sceneboard` 的
**`WallpaperServiceExtAbility`** 提供（module `themecomponent`），应用需要绑定该服务。但它要求：

```
permissions: ["ohos.permission.ACTIVATE_THEME_PACKAGE"]
```

实测把这个权限写进 manifest 后，**安装直接失败**：

```
install failed due to grant request permissions failed.
PermissionName: ohos.permission.ACTIVATE_THEME_PACKAGE
```

即该服务受**系统级权限**保护，第三方应用无法绑定。**相册之所以能设壁纸，正因为它是有系统权限的系统应用。**

> 教训（和华为 EMUI 那次一样）：**判断壁纸是否设置成功，只能看屏幕**。
> API 返回值、dumpsys、应用日志的"成功"都不是证据。本项目因此把两种"假成功"都记在案。

桥接层对鸿蒙的**自动**设置一律返回 `WALLPAPER_NOT_SUPPORTED`（它确实做不到），
但面板会引导用户走上面那条**经应用转存相册**的可用链路。

### 结论：自动设置不可行，但有**已验证可用**的替代链路

系统层的两条路都被堵死（`setWallpaper` 是空存根；`WallpaperServiceExtAbility` 需要系统级权限）。
但**相册本身能设壁纸** —— 相册是有系统权限的系统应用。所以可行的链路是"把图片送进相册，用户在相册里设置"。

难点在于**怎么把图片送进相册**：

| 尝试 | 结果 |
|---|---|
| hdc 直接写入媒体库 | ❌ `/storage/media/100/local/files/Photo` 拒绝 hdc 写入；能写的 `Docs` 不被媒体库收录 |
| 应用读 hdc 写的文件 | ❌ 应用读不到 `/data/local/tmp`（13900002） |
| **应用自己写相册** | ✅ **可行** |

最终方案（`apps/harmony-helper`）：

1. 面板点「保存到相册」→ Bridge 裁剪好图片并保留（`GET /api/v1/wallpaper/latest-image`）
2. `hdc rport tcp:18766 tcp:18765` 让**手机反向访问主机**的 Bridge
3. 手机上的「PSMW 壁纸助手」应用下载图片并显示
4. 用户点应用里的 **`SaveButton`**（鸿蒙安全控件：**免权限**，由点击授权写入相册）
5. 用户在相册里选「设为壁纸」

**已实测验证**：应用取到 20224 字节，保存后 `mediatool` 在媒体库中查到该资源，
且回读的 sha256 与 Bridge 裁剪产物**逐字节一致**。

> 全程不需要 `ACTIVATE_THEME_PACKAGE` 等系统级权限，也不需要 hdc 写媒体库。

### 构建这个应用

工程在 `apps/harmony-helper`。**注意两点**：

1. **路径不能含空格** —— DevEco 会报 `Invalid path`。含空格的仓库路径需要先复制到无空格目录再构建。
2. **签名 profile 绑定 bundle 名** —— DevEco 的「自动生成签名」目前产出的是绑定
   `com.example.myapplication` 的 profile，所以工程的 `bundleName` 也设成了它。
   要换成正式包名，需在 DevEco 里重新生成签名。

构建：

```bash
cd apps/harmony-helper
hvigorw --mode module -p product=default -p buildMode=debug assembleHap --no-daemon
hdc install -r entry/build/default/outputs/default/entry-default-signed.hap
hdc rport tcp:18766 tcp:18765      # 让手机能访问主机的 Bridge
```

## 还没做到

- **其他 Android 品牌的锁屏**：未验证。按 §40 一律报 `canSetLock: false`，验证通过后才放开。
- **主屏设置在非华为设备上**未验证（用的是公开 API，风险较低，但仍是未验证）。
- **插件面板的交互功能**尚未逐项验证（面板已确认能在 Photoshop 中加载）。

## 在 Photoshop 中加载插件

UXP 插件**不会**出现在「窗口 → 扩展（旧版）」—— 那是 CEP 的位置。UXP 面板在
**「增效工具」(Plug-ins)** 菜单下。

**重要**：Windows 安装包只安装 PhoneBridge，并把插件文件放在安装目录的 `plugin\` 下，
**不会**向 Photoshop 注册插件。需要单独加载：

```powershell
# 无需 Adobe 账号：复制到 UXP 的旁加载目录，然后重启 Photoshop
$dest = "$env:APPDATA\Adobe\UXP\Plugins\External\com.psmobilewallpaper.panel"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item "$env:ProgramFiles\PS Mobile Wallpaper\plugin\*" $dest -Force
```

重启 Photoshop → 菜单栏 → **增效工具** → **PS Mobile Wallpaper**。

也可以使用官方的 UXP Developer Tool（需 Creative Cloud 桌面版 + Adobe 账号）：
`Add Plugin` → 选择 `plugin\manifest.json` → `Load`。

面板首次使用需要在 Token 输入框填入 `%AppData%\PSMobileWallpaper\auth.token` 的内容
（点「从文件读取」会直接打开该目录）。UXP 沙箱不允许插件自行读取 `%AppData%`。

## 验证方式

```bash
# 单元测试（含线上格式契约与全部裁剪模式几何）
dotnet test tests/unit/PSMobileWallpaper.Tests/PSMobileWallpaper.Tests.csproj

# 集成测试：需要真机；无设备时自动跳过并保持绿色
dotnet test tests/integration/PSMobileWallpaper.IntegrationTests/PSMobileWallpaper.IntegrationTests.csproj

# 端到端（需先启动 Bridge）
python scripts/phase5-e2e-check.py    # 设备识别 + WS 事件 + 裁剪
python scripts/crop-modes-check.py    # §11 全部裁剪模式的像素级验证
python scripts/wallpaper-check.py   # §17 壁纸设置：主屏生效、锁屏如实拒绝
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
