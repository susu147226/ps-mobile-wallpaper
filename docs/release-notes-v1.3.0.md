# PS Mobile Wallpaper v1.3.0

让**换一台电脑**接近零配置。

## 新增：adb 随安装包分发

之前每台新电脑都要自己装 Android SDK Platform Tools 并配置路径。现在 adb 直接打进安装包
（文档 §27 早就预留了 `runtime/adb` 这个位置）：

- 安装后在 `<安装目录>\runtime\adb\` 下
- Bridge **优先从随包目录查找**，其次才是 PATH
- 因此 `config.json` 的 `adb.path` **留空即可**，开箱就能连 Android 手机

adb 是 Apache-2.0 许可，可再分发。

**hdc 没有打包** —— 它随 DevEco Studio 分发，没有再分发授权。鸿蒙用户仍需自行安装 DevEco，
把 `hdc.path` 指向其目录（或加入 PATH）。

## 新增：自动建立反向端口转发

鸿蒙的配套应用需要反向访问电脑上的 Bridge。之前这条命令要**每台新电脑手动执行一次**：

```bash
hdc rport tcp:18766 tcp:18765
```

现在 Bridge 会自己做：

- 检测到鸿蒙设备接入时自动建立
- 启动时对已连接的设备也会补建
- 设备断开时清理记录

实测日志：

```
HarmonyOS reverse port forward enabled: device tcp:18766 -> host tcp:18765.
Reverse port forward established for 88Z9K26525078813: device tcp:18766 -> host tcp:18765.
```

## 换电脑的完整步骤（本版后）

1. 运行 MSI 安装
2. 重启 Photoshop → 面板出现在「增效工具」菜单
3. 插上 Android 手机 → **直接可用**（adb 已随包）
4. 如需鸿蒙：装 DevEco Studio，把 `hdc.path` 指过去 → 其余自动

不再需要：装 .NET、配 adb 路径、配认证 Token、手动敲端口转发命令。

## 验证

- 随包 adb：`adb.path` 留空时**零告警**，Android 手机正常识别
- 自动转发：日志确认建立成功，`hdc fport ls` 可见 `tcp:18766 tcp:18765 [Reverse]`
- 单元测试 132 项、集成测试 14 项全通过
