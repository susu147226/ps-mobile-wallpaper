import os from "os";
import { storage } from "uxp";
import { BridgeClient, BridgeError } from "../api/bridgeClient";

/**
 * UXP surfaces these in Photoshop's UXPLogs file, which is the only way to diagnose the panel —
 * it has no visible console. Everything the panel decides should be traceable from there.
 */
function log(message: string, ...rest: unknown[]): void {
  console.log(`[PSMW] ${message}`, ...rest);
}

import type {
  BridgeEvent,
  CropMode,
  DeviceInfo,
  DeviceState,
  OutputFormat,
  PreparedWallpaper,
  TransferEventPayload,
} from "../models/types";
import { DEVICE_STATE_LABELS } from "../models/types";
import { exportActiveDocument, readActiveDocument } from "../services/photoshopService";
import { formatProgressBar, formatSize } from "../utils/format";

/** Spec §31 crop-mode labels, matching the option text in index.html. */
const CROP_MODE_LABELS: Record<CropMode, string> = {
  "center-crop": "居中裁剪",
  "center-fit": "完整适应",
  stretch: "拉伸铺满",
  "top-crop": "顶部裁剪",
  "bottom-crop": "底部裁剪",
};

/** Spec §32: which dot colour each state gets. */
const STATE_DOT_CLASS: Record<DeviceState, string> = {
  Unknown: "dot--error",
  Disconnected: "dot--idle",
  Connecting: "dot--warn",
  Connected: "dot--ok",
  Unauthorized: "dot--error",
  Offline: "dot--warn",
  Error: "dot--error",
};

export class PanelController {
  private readonly client = new BridgeClient();

  private devices: DeviceInfo[] = [];
  private selectedDeviceId = "";
  private prepared: PreparedWallpaper | null = null;
  private busy = false;
  private disposed = false;

  private readonly elements: {
    deviceSelect: HTMLSelectElement;
    statusDot: HTMLElement;
    statusText: HTMLElement;
    deviceMeta: HTMLElement;
    displaySize: HTMLElement;
    canvasSize: HTMLElement;
    canvasMeta: HTMLElement;
    cropMode: HTMLSelectElement;
    outputFormat: HTMLSelectElement;
    tokenInput: HTMLInputElement;
    tokenPick: HTMLButtonElement;
    refresh: HTMLButtonElement;
    preview: HTMLButtonElement;
    send: HTMLButtonElement;
    setLock: HTMLButtonElement;
    progress: HTMLElement;
    progressText: HTMLElement;
    progressBar: HTMLElement;
    message: HTMLElement;
  };

  constructor() {
    this.elements = {
      deviceSelect: requireElement<HTMLSelectElement>("device-select"),
      statusDot: requireElement("device-status-dot"),
      statusText: requireElement("device-status-text"),
      deviceMeta: requireElement("device-meta"),
      displaySize: requireElement("display-size"),
      canvasSize: requireElement("canvas-size"),
      canvasMeta: requireElement("canvas-meta"),
      cropMode: requireElement<HTMLSelectElement>("crop-mode"),
      outputFormat: requireElement<HTMLSelectElement>("output-format"),
      tokenInput: requireElement<HTMLInputElement>("token-input"),
      tokenPick: requireElement<HTMLButtonElement>("token-pick"),
      refresh: requireElement<HTMLButtonElement>("btn-refresh"),
      preview: requireElement<HTMLButtonElement>("btn-preview"),
      send: requireElement<HTMLButtonElement>("btn-send"),
      setLock: requireElement<HTMLButtonElement>("btn-set-lock"),
      progress: requireElement("progress"),
      progressText: requireElement("progress-text"),
      progressBar: requireElement("progress-bar"),
      message: requireElement("message"),
    };
  }

  public async start(): Promise<void> {
    this.bindEvents();
    this.updateCanvasInfo();
    this.updateActionAvailability();

    log("panel started; token set:", this.client.getToken().length > 0);

    const healthy = await this.client.checkHealth();
    log("bridge /health reachable:", healthy);

    if (!healthy) {
      this.setMessage(
        "未能连接 PhoneBridge（http://127.0.0.1:18765）。请先启动 PhoneBridge，再点击「获取手机尺寸」。",
        "error"
      );
      this.setStatus("Disconnected");
      return;
    }

    this.setMessage("已连接 PhoneBridge。", "ok");
    await this.refreshDevices();
    this.connectEvents();
  }

  public dispose(): void {
    this.disposed = true;
    this.client.disconnectEvents();
  }

  private bindEvents(): void {
    this.elements.deviceSelect.addEventListener("change", () => {
      this.selectedDeviceId = this.elements.deviceSelect.value;
      this.prepared = null;
      this.updateSelectedDevice();
      this.updateActionAvailability();
    });

    this.elements.tokenInput.addEventListener("change", () => {
      this.client.setToken(this.elements.tokenInput.value);
      log("token applied; length:", this.client.getToken().length);

      this.setMessage(
        this.client.getToken() ? "已应用本地认证 Token。" : "已清空本地认证 Token。",
        "info"
      );
    });

    this.elements.tokenPick.addEventListener("click", () => {
      void this.pickTokenFile();
    });

    this.elements.refresh.addEventListener("click", () => void this.guard(() => this.refreshDevices(true)));
    this.elements.preview.addEventListener("click", () => void this.guard(() => this.preview()));
    this.elements.send.addEventListener("click", () => void this.guard(() => this.transfer()));
    this.elements.setLock.addEventListener("click", () => void this.guard(() => this.setLockWallpaper()));
    this.elements.outputFormat.addEventListener("change", () => {
      // Changing the format invalidates anything already cropped.
      this.prepared = null;
      this.updateActionAvailability();
    });

    this.elements.cropMode.addEventListener("change", () => {
      // A different crop mode means the previous result no longer reflects the settings.
      this.prepared = null;
      this.updateActionAvailability();
    });
  }

  /** Spec §5.1. Re-enumerates devices and pulls the selected phone's screen size (spec §6). */
  private async refreshDevices(explicit: boolean = false): Promise<void> {
    if (!(await this.client.checkHealth())) {
      this.setStatus("Disconnected");
      this.setMessage("PhoneBridge 未运行。", "error");
      return;
    }

    try {
      this.devices = await this.client.getDevices();
    } catch (error) {
      log("GET /devices failed:", error instanceof BridgeError ? error.errorCode : String(error));
      this.setMessage(describeError(error), "error");
      return;
    }

    log("GET /devices returned", this.devices.length, "device(s)");

    const previous = this.selectedDeviceId;
    this.renderDeviceOptions();

    if (this.devices.length === 0) {
      this.setStatus("Disconnected");
      this.setMessage("未检测到手机。请通过 USB 连接手机并确认已开启 USB 调试。", "info");
      this.updateActionAvailability();
      return;
    }

    if (!this.devices.some((device) => device.id === previous)) {
      this.selectedDeviceId = this.pickDefaultDevice()?.id ?? "";
    }

    this.elements.deviceSelect.value = this.selectedDeviceId;
    this.updateSelectedDevice();

    if (explicit) {
      await this.refreshDisplay();
    }

    this.updateActionAvailability();
  }

  private renderDeviceOptions(): void {
    const select = this.elements.deviceSelect;
    select.innerHTML = "";

    if (this.devices.length === 0) {
      const option = document.createElement("option");
      option.value = "";
      option.textContent = "未检测到设备";
      select.appendChild(option);
      select.disabled = true;
      return;
    }

    for (const device of this.devices) {
      const option = document.createElement("option");
      option.value = device.id;
      option.textContent = `${device.brand} ${device.model}`.trim() || device.id;
      select.appendChild(option);
    }

    select.disabled = false;
  }

  /** Prefers a usable device over one that is merely present. */
  private pickDefaultDevice(): DeviceInfo | undefined {
    return (
      this.devices.find((device) => device.state === "Connected") ??
      this.devices.find((device) => device.state === "Unauthorized") ??
      this.devices[0]
    );
  }

  private updateSelectedDevice(): void {
    const device = this.selectedDevice;
    if (!device) {
      this.setStatus("Disconnected");
      this.elements.deviceMeta.textContent = "";
      this.elements.displaySize.textContent = "—";
      return;
    }

    this.setStatus(device.state);

    const parts: string[] = [];
    if (device.os) {
      parts.push(`${device.os} ${device.osVersion}`.trim());
    }
    parts.push(device.transport);
    parts.push(device.id);
    this.elements.deviceMeta.textContent = parts.join(" · ");

    this.elements.displaySize.textContent = device.display
      ? formatSize(device.display.width, device.display.height)
      : "—";
  }

  /** Spec §9: the phone's size is what the canvas gets cropped to. */
  private async refreshDisplay(): Promise<void> {
    const device = this.selectedDevice;
    if (!device) {
      return;
    }

    try {
      const display = await this.client.getDisplay(device.id);

      device.display = display;
      this.elements.displaySize.textContent = formatSize(display.width, display.height);
      this.prepared = null;
      this.setMessage(`已获取 ${device.brand} ${device.model} 的屏幕尺寸。`, "ok");
    } catch (error) {
      this.elements.displaySize.textContent = "—";
      this.setMessage(describeError(error), "error");
    }

    this.updateActionAvailability();
  }

  private updateCanvasInfo(): void {
    try {
      const info = readActiveDocument();
      this.elements.canvasSize.textContent = formatSize(info.width, info.height);
      this.elements.canvasMeta.textContent =
        `${info.name} · ${info.colorMode} · ${Math.round(info.resolution)} ppi`;
    } catch {
      this.elements.canvasSize.textContent = "—";
      this.elements.canvasMeta.textContent = "没有打开的文档";
    }
  }

  /** Spec §8 + §10: export the canvas, then have the bridge center-crop it to the screen. */
  private async preview(): Promise<void> {
    const device = this.selectedDevice;
    if (!device) {
      this.setMessage("请先选择设备。", "error");
      return;
    }

    this.updateCanvasInfo();
    this.setMessage("正在导出画布...", "info");

    const format = this.elements.outputFormat.value as OutputFormat;
    const mode = this.elements.cropMode.value as CropMode;
    const exportedPath = await exportActiveDocument(format);

    const display = device.display ?? (await this.client.getDisplay(device.id));
    device.display = display;
    this.elements.displaySize.textContent = formatSize(display.width, display.height);

    this.prepared = await this.client.prepareWallpaper(
      device.id,
      exportedPath,
      display.width,
      display.height,
      mode
    );

    if (!this.prepared.success) {
      this.setMessage(this.prepared.message || "裁剪失败。", "error");
      return;
    }

    this.setMessage(
      `裁剪完成（${CROP_MODE_LABELS[mode] ?? mode}）：${formatSize(this.prepared.width, this.prepared.height)}\n${this.prepared.imagePath}`,
      "ok"
    );

    this.updateActionAvailability();
  }

  /** Spec §13: push the prepared image over ADB/HDC into the phone gallery. */
  private async transfer(): Promise<void> {
    const prepared = await this.ensurePrepared();
    if (!prepared) {
      return;
    }

    this.showProgress("正在传输...", 0);
    this.setMessage("正在传输到手机...", "info");

    try {
      const result = await this.client.sendWallpaper(prepared.deviceId, prepared.imagePath);
      this.setMessage(result.message, "ok");
    } catch (error) {
      this.setMessage(describeError(error), "error");
    } finally {
      this.hideProgress();
    }
  }

  /** Spec §17 / §20: save to the gallery and set the lock screen. */
  private async setLockWallpaper(): Promise<void> {
    const prepared = await this.ensurePrepared();
    if (!prepared) {
      return;
    }

    this.setMessage("正在设置锁屏壁纸...", "info");

    try {
      const result = await this.client.setLockWallpaper(prepared.deviceId, prepared.imagePath);
      this.setMessage(result.message, "ok");
    } catch (error) {
      this.setMessage(describeError(error), "error");
    }
  }

  /** Reuses an existing crop when one is available, so the canvas is not exported twice. */
  private async ensurePrepared(): Promise<{ deviceId: string; imagePath: string } | null> {
    const device = this.selectedDevice;
    if (!device) {
      this.setMessage("请先选择设备。", "error");
      return null;
    }

    if (!this.prepared?.success || !this.prepared.imagePath) {
      await this.preview();
    }

    if (!this.prepared?.success || !this.prepared.imagePath) {
      return null;
    }

    return { deviceId: device.id, imagePath: this.prepared.imagePath };
  }

  private connectEvents(): void {
    this.client.connectEvents(
      (event: BridgeEvent) => this.handleEvent(event),
      (connected: boolean) => {
        if (!connected) {
          this.setStatus(this.selectedDevice?.state ?? "Disconnected");
        }
      }
    );
  }

  /** Spec §22. Device changes trigger a refresh; transfer events drive the progress bar (spec §33). */
  private handleEvent(event: BridgeEvent): void {
    switch (event.event) {
      case "device.connected":
      case "device.disconnected":
      case "device.updated":
        void this.refreshDevices();
        break;

      case "transfer.progress": {
        const payload = event.data as TransferEventPayload;
        if (payload.totalBytes > 0) {
          this.showProgress("正在传输...", payload.percent, payload.bytesTransferred, payload.totalBytes);
        }
        break;
      }

      case "transfer.started":
        this.showProgress("正在传输...", 0);
        break;

      case "transfer.completed":
      case "transfer.failed":
        this.hideProgress();
        break;

      default:
        break;
    }
  }

  /**
   * Reads the token file the user picks. UXP cannot reach %AppData% itself, so the user has to
   * choose the file once; the picker opens straight in the bridge's folder to make that easy.
   */
  private async pickTokenFile(): Promise<void> {
    try {
      const file = await storage.localFileSystem.getFileForOpening({
        initialLocation: `${os.homedir()}\\AppData\\Roaming\\PSMobileWallpaper`,
      });

      if (!file) {
        return;
      }

      const token = String(await file.read()).trim();
      log("token file picked; length:", token.length);

      if (token) {
        this.elements.tokenInput.value = token;
        this.client.setToken(token);
        this.setMessage("已从文件读取并应用 Token。", "ok");
      }
    } catch (error) {
      log("token file pick failed:", String(error));
      this.setMessage(
        "无法读取 Token 文件。请手动打开 %AppData%\\PSMobileWallpaper\\auth.token 并粘贴其内容。",
        "error"
      );
    }
  }

  private get selectedDevice(): DeviceInfo | undefined {
    return this.devices.find((device) => device.id === this.selectedDeviceId);
  }

  private updateActionAvailability(): void {
    const device = this.selectedDevice;
    const usable = !!device && device.state === "Connected";
    const hasDisplay = !!device?.display;

    this.elements.refresh.disabled = this.busy;
    this.elements.preview.disabled = this.busy || !usable;
    this.elements.send.disabled = this.busy || !usable || !hasDisplay;
    this.elements.setLock.disabled = this.busy || !usable || !hasDisplay;
  }

  private setStatus(state: DeviceState): void {
    this.elements.statusText.textContent = DEVICE_STATE_LABELS[state] ?? state;
    this.elements.statusDot.className = `dot ${STATE_DOT_CLASS[state] ?? "dot--idle"}`;
  }

  private setMessage(text: string, kind: "ok" | "error" | "info"): void {
    this.elements.message.textContent = text;
    this.elements.message.className = `message message--${kind}`;
  }

  private showProgress(label: string, percent: number, bytesTransferred = 0, totalBytes = 0): void {
    this.elements.progress.hidden = false;
    this.elements.progressText.textContent = label;

    const suffix = totalBytes > 0 ? `  ${formatBytesShort(bytesTransferred)} / ${formatBytesShort(totalBytes)}` : "";
    this.elements.progressBar.textContent = `${formatProgressBar(percent)} ${Math.round(percent)}%${suffix}`;
  }

  private hideProgress(): void {
    this.elements.progress.hidden = true;
  }

  /** Prevents overlapping bridge calls and keeps the buttons consistent while one is running. */
  private async guard(operation: () => Promise<void>): Promise<void> {
    if (this.busy) {
      return;
    }

    this.busy = true;
    this.updateActionAvailability();

    try {
      await operation();
    } catch (error) {
      this.setMessage(describeError(error), "error");
    } finally {
      this.busy = false;

      if (!this.disposed) {
        this.updateCanvasInfo();
        this.updateActionAvailability();
      }
    }
  }
}

function requireElement<T extends HTMLElement = HTMLElement>(id: string): T {
  const element = document.getElementById(id);
  if (!element) {
    throw new Error(`面板元素缺失：${id}`);
  }

  return element as T;
}

function describeError(error: unknown): string {
  if (error instanceof BridgeError) {
    return `${error.message}${error.errorCode ? `\n[${error.errorCode}]` : ""}`;
  }

  if (error instanceof Error) {
    return error.message;
  }

  return String(error);
}

function formatBytesShort(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(0)} KB`;
  }

  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
