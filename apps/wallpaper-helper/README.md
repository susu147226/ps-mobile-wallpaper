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
  --es imagePath /sdcard/Download/PSMobileWallpaper/xxx.png \
  --es target lock|home|both
```

`am start` 无法返回值，所以结果写入 **应用私有目录** 的 `files/psmw-result.json`，
Bridge 用 `run-as` 读回：

```json
{ "success": true, "message": "Applied to: lock.", "errorCode": null, "applied": ["lock"] }
```

## 实测中踩到的几个坑（都已解决，记下来免得重犯）

1. **`setStream` 的隐藏重载返回 `int`，不是 `void`**。
   反射 dump 真机方法表后确认签名为
   `setStream(InputStream, Rect, boolean, int) -> int`。JNI 描述符末尾必须是 `I`，
   用 `CallIntMethod` 调用。写成 `V` + `CallVoidMethod` 会得到
   `no non-static method ...`。

2. **`targetSdkVersion` 必须写在 `AndroidManifest.xml` 里**。
   只写 csproj 的 `<TargetSdkVersion>` 不生效，会被默认成编译用的 SDK（36）。
   而 Android 9+ 的隐藏 API 拦截正是按 targetSdk 判定的，≥28 会直接封掉上面那个方法。

3. **`/sdcard/Android/data/<pkg>/` 连应用自己都不能用原始路径访问**（Android 11+）。
   所以待设置的图片推到 `/sdcard/Download/PSMobileWallpaper/`，不要推到这个目录。

4. **开启 AOT 会导致运行时崩溃**：`UnsatisfiedLinkError: No implementation found for
   ... n_onCreate`。构建时必须加 `-p:RunAOTCompilation=false`。

5. **必须 `android:debuggable="true"`**，否则 Bridge 无法用 `run-as` 读回结果。
   该 APK 仅侧载分发、不进入 Play，见 AndroidManifest.xml 中的说明。

6. **锁屏设置在这台 EMUI 设备上无效 —— 而且是"假成功"**。
   `setStream(..., FLAG_LOCK)` 返回成功，`dumpsys wallpaper` 里 Lock 壁纸 id 也确实变了，
   但锁屏**实际没有任何变化**。原因是 EMUI 用自己的主题引擎渲染锁屏壁纸，其 provider 受
   `com.huawei.android.thememanager.permission.THEME_PROVIDER_ACCESS`（signature 级）保护。

   **教训：判断壁纸是否设置成功，必须看屏幕，不能看 API 返回值或 dumpsys。**
   Bridge 因此把 `canSetLock` 固定为 `false` 并拒绝该操作，避免误导。

## 构建

```bash
# 首次需要 Android SDK + JDK（约 740 MB）
dotnet build -t:InstallAndroidDependencies -f net10.0-android \
  -p:AndroidSdkDirectory="C:\Android\Sdk" -p:JavaSdkDirectory="C:\Android\Jdk" \
  -p:AcceptAndroidSDKLicenses=True

dotnet build -f net10.0-android -c Release \
  -p:AndroidSdkDirectory="C:\Android\Sdk" -p:JavaSdkDirectory="C:\Android\Jdk" \
  -p:RunAOTCompilation=false -p:AndroidEnableProfiledAot=false
```

产物：`bin/Release/net10.0-android/android-arm64/com.psmobilewallpaper.helper-Signed.apk`

## 安装到设备

```bash
adb install --no-incremental -g -r <apk>
```

`-g` 授予 `READ_EXTERNAL_STORAGE`（helper 需要读取推送过去的图片）。
`--no-incremental` 规避部分机型上的原生库加载问题。
