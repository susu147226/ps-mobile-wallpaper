/**
 * Mirror of the bridge's wire model. Kept in sync by hand with the C# types in
 * PSMobileWallpaper.Domain — spec §5.4, §6, §7, §19, §29.
 */

/** Spec §5.2 raw states plus the UI states in spec §32. */
export type DeviceState =
  | "Unknown"
  | "Disconnected"
  | "Connecting"
  | "Connected"
  | "Unauthorized"
  | "Offline"
  | "Error";

/** Spec §5.4 */
export type DeviceTransportKind = "ADB" | "HDC";

export type ScreenOrientation = "Portrait" | "Landscape";

/** Spec §6 */
export interface DisplayInfo {
  width: number;
  height: number;
  density: number;
  rotation: number;
  orientation: ScreenOrientation;
}

/** Spec §5.4 / §28 */
export interface DeviceInfo {
  id: string;
  brand: string;
  manufacturer: string;
  model: string;
  os: string;
  osVersion: string;
  transport: DeviceTransportKind;
  state: DeviceState;
  display?: DisplayInfo | null;
}

/** Spec §7 */
export interface PhotoshopDocumentInfo {
  name: string;
  width: number;
  height: number;
  resolution: number;
  colorMode: string;
}

/** Spec §19 */
export interface WallpaperCapabilities {
  canSetLock: boolean;
  canSetHome: boolean;
  canSetBoth: boolean;
  canSaveToGallery: boolean;
  requiresUserConfirmation: boolean;
}

/** Spec §29 */
export interface WallpaperResult {
  success: boolean;
  message: string;
  errorCode?: string | null;
  deviceId?: string | null;
}

/** Spec §30 */
export interface ApiError {
  success: false;
  errorCode: string;
  message: string;
}

/** Response of <c>POST /api/v1/wallpaper/prepare</c>. */
export interface PreparedWallpaper {
  success: boolean;
  imagePath?: string | null;
  width: number;
  height: number;
  message: string;
  errorCode?: string | null;
}

/** Spec §11. The modes the panel exposes; `custom` needs a region editor and is API-only for now. */
export type CropMode = "center-crop" | "center-fit" | "stretch" | "top-crop" | "bottom-crop";

/** Spec §2.5 / §8 */
export type OutputFormat = "png" | "jpg";

/** Spec §22 */
export interface BridgeEvent<T = unknown> {
  event: string;
  data: T;
}

export interface DeviceEventPayload {
  deviceId: string;
  brand: string;
  model: string;
}

export interface TransferEventPayload {
  deviceId: string;
  direction: string;
  localPath?: string | null;
  remotePath?: string | null;
  bytesTransferred: number;
  totalBytes: number;
  percent: number;
}

/** Spec §32. What the status row shows. */
export const DEVICE_STATE_LABELS: Record<DeviceState, string> = {
  Unknown: "设备异常",
  Disconnected: "未连接",
  Connecting: "正在连接",
  Connected: "已连接",
  Unauthorized: "授权失败",
  Offline: "设备离线",
  Error: "设备异常",
};
