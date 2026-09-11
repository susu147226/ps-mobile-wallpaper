# PSMW Wallpaper Helper (Android)

A minimal Android app that lets PhoneBridge set a phone wallpaper through the official
`WallpaperManager` API.

## 为什么需要它

`adb shell` 无法设置壁纸：

- `cmd wallpaper` 在这些设备上只返回 `No shell command implementation.`
- `com.android.shell` 虽然持有 `SET_WALLPAPER` 权限，但**没有任何命令行出口**去使用它
- `service call wallpaper` 需要传文件描述符，且事务码随版本变化，不可靠

因此需要一个持有 `SET_WALLPAPER` 权限的普通应用代为执行。这是**使用官方 API**，
不是破解：不 root、不改系统镜像、不绕过任何安全检查（spec §42）。

## 关于 `targetSdkVersion = 27`

设置**锁屏**壁纸需要 `WallpaperManager.setStream(InputStream, Rect, boolean, int)` 这个
隐藏重载（公开 API 只能设置主屏）。Android 9+ 对 `targetSdkVersion >= 28` 的应用封锁非 SDK
接口，所以本应用刻意把 `targetSdkVersion` 设为 27 以保持豁免。

本应用通过侧载分发，不面向 Play 商店，因此使用较旧的 target 没有实际代价。

## 调用约定

由 PhoneBridge 通过 `am start` 启动，无界面、无启动图标：

```
adb shell am start \
  -n com.psmobilewallpaper.helper/.SetWallpaperActivity \
  --es imagePath /sdcard/Download/xxx.png \
  --es target lock|home|both \
  --es resultPath /sdcard/Download/xxx.json
```

`am start` 无法返回值，所以结果以 JSON 写入 `resultPath`：

```json
{ "success": true, "message": "Applied to: lock.", "errorCode": null, "applied": ["lock"] }
```

## 构建

```bash
dotnet build -f net10.0-android -c Release
```

需要 .NET Android workload 与 Android SDK：

```bash
dotnet workload install android
```

## 安装到设备

```bash
adb install -r bin/Release/net10.0-android/com.psmobilewallpaper.helper-Signed.apk
```

首次安装后 `SET_WALLPAPER` 会自动授予（属 normal 权限，无需运行时申请）。
