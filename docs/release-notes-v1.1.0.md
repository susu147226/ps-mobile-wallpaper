# PS Mobile Wallpaper v1.1.0

修掉了 v1.0.0 中几个**装完就不工作**的问题，并把壁纸能力改成经过真机验证的真实声明。

## ⚠️ 升级前必读

v1.0.0 是 **per-machine** 安装，v1.1.0 改为 **per-user**，因此 UpgradeCode 不同，**不会自动升级**。
请先卸载旧版：

```
设置 → 应用 → 已安装的应用 → PS Mobile Wallpaper PhoneBridge → 卸载
```

然后安装 v1.1.0。旧版配置（`%AppData%\PSMobileWallpaper\`）会保留。

## v1.0.0 的三个问题

| 问题 | 原因 | 现状 |
|---|---|---|
| 装完在 Photoshop 里**看不到插件** | 安装包只放了文件，**从未向 Photoshop 注册插件** | ✅ 现在自动部署到 UXP 旁加载目录 |
| 即使用 UDT 也**加载不了插件** | `manifest.json` 的 `host` 写成了数组，3P 插件必须是对象 | ✅ 已修正 |
| **锁屏壁纸设置是假成功** | API 返回成功、dumpsys 也变了，但屏幕没变 | ✅ 现在如实报告不支持 |

## 本次改动

### 1. 安装包改为 per-user，并自动部署插件

- 安装到 `%LocalAppData%\PS Mobile Wallpaper\`，**无需管理员权限**
- 插件自动部署到 `%AppData%\Adobe\UXP\Plugins\External\com.psmobilewallpaper.panel\`
- **装完重启 Photoshop，插件出现在「增效工具」菜单**，无需 Adobe 账号

这与文档 §2.7 / §23 也更一致 —— 配置本来就存在**用户**的 `%AppData%`。

> 注意：UXP 插件**不会**出现在「窗口 → 扩展（旧版）」，那是 CEP 的位置。

### 2. 锁屏壁纸：从"声称支持"改为"如实拒绝"

真机实测（Huawei JAD-AL80 / Android 12 / EMUI）：

| 目标 | 结果 |
|---|---|
| 主屏 | ✅ 实际生效 |
| 锁屏 | ❌ **实际无效** |

EMUI 的锁屏壁纸由华为主题引擎渲染，其 provider 受
`com.huawei.android.thememanager.permission.THEME_PROVIDER_ACCESS`（signature 级权限）保护，
第三方应用无法写入。

**这里有两个非常有迷惑性的假阳性证据**：helper 的 `setStream(..., FLAG_LOCK)` 返回成功，
`dumpsys wallpaper` 里 Lock 壁纸 id 也确实变了 —— 但锁屏**没有任何变化**。

因此能力声明现在如实反映屏幕上的实际效果：

```json
{ "canSetLock": false, "canSetHome": true, "canSetBoth": false, "canSaveToGallery": true }
```

设置锁屏会返回 `WALLPAPER_NOT_SUPPORTED` 并说明原因，而不是返回一个用户永远看不到的成功。

### 3. 附带 Android 壁纸 helper

`adb shell` 无法设置壁纸（`cmd wallpaper` 在该机型上无实现，shell 用户虽持 `SET_WALLPAPER`
权限但无命令行出口），因此附带一个 helper APK，通过**官方 WallpaperManager API** 代为设置。
Bridge 会在需要时自动把它装到手机上。

这不是破解：不 root、不改系统镜像、不绕过安全检查。helper 的细节与踩坑记录见
`apps/wallpaper-helper/README.md`。

### 4. 其他修复

- `/health` 之前返回硬编码的 `"1.0.0"`，导致无法判断实际运行的是哪个版本 —— 现在读取程序集版本
- 面板增加 `[PSMW]` 日志（之前一行都没有，导致无法从 UXPLogs 诊断）
- Token 文件选择器直接打开到 `%AppData%\PSMobileWallpaper`

## 验证

- 单元测试 132 项、集成测试 14 项（两台真机，无跳过）
- 安装包实测：per-user 安装 → 插件被 Photoshop 加载 → helper APK 就位 → 卸载干净
- 壁纸：`scripts/wallpaper-check.py` 断言"主屏生效、锁屏如实拒绝"

## 仍然不支持

- **HarmonyOS 的壁纸设置**：其 `WallpaperManagerService` 经 hidumper 不暴露接口，只有相册保存可用
- **其他 Android 品牌的锁屏**：未验证，一律报不支持（验证通过后才放开）
- **其他 Android 品牌的主屏**：用的是公开 API，风险较低，但仍未逐一验证
