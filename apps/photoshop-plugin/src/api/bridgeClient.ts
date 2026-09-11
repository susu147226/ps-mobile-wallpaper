import type {
  ApiError,
  BridgeEvent,
  CropMode,
  DeviceInfo,
  DisplayInfo,
  PreparedWallpaper,
  WallpaperCapabilities,
  WallpaperResult,
} from "../models/types";

/** Spec §2.4 default endpoints. */
export const DEFAULT_BRIDGE_HTTP = "http://127.0.0.1:18765";
export const DEFAULT_BRIDGE_WS = "ws://127.0.0.1:18765/ws";
export const API_PREFIX = "/api/v1";

export class BridgeError extends Error {
  public readonly errorCode: string;
  public readonly status: number;

  constructor(errorCode: string, message: string, status: number) {
    super(message);
    this.name = "BridgeError";
    this.errorCode = errorCode;
    this.status = status;
  }
}

export interface BridgeClientOptions {
  baseUrl?: string;
  webSocketUrl?: string;
  /** Spec §23 local authentication token. */
  token?: string;
}

/**
 * Talks to PhoneBridge over loopback. All bridge access goes through this class so the REST shape
 * and the auth header live in exactly one place.
 */
export class BridgeClient {
  private baseUrl: string;
  private webSocketUrl: string;
  private token: string;
  private socket: WebSocket | null = null;

  constructor(options: BridgeClientOptions = {}) {
    this.baseUrl = options.baseUrl ?? DEFAULT_BRIDGE_HTTP;
    this.webSocketUrl = options.webSocketUrl ?? DEFAULT_BRIDGE_WS;
    this.token = options.token ?? "";
  }

  public setToken(token: string): void {
    this.token = token.trim();
  }

  public getToken(): string {
    return this.token;
  }

  /** `GET /health` — used to detect whether the bridge is running before doing anything else. */
  public async checkHealth(): Promise<boolean> {
    try {
      const response = await fetch(`${this.baseUrl}/health`, { method: "GET" });

      return response.ok;
    } catch {
      return false;
    }
  }

  /** `GET /api/v1/devices` */
  public async getDevices(): Promise<DeviceInfo[]> {
    return this.request<DeviceInfo[]>("GET", "/devices");
  }

  /** `GET /api/v1/devices/{id}` */
  public async getDevice(deviceId: string): Promise<DeviceInfo> {
    return this.request<DeviceInfo>("GET", `/devices/${encodeURIComponent(deviceId)}`);
  }

  /** `GET /api/v1/devices/{id}/display` (spec §6) */
  public async getDisplay(deviceId: string): Promise<DisplayInfo> {
    return this.request<DisplayInfo>("GET", `/devices/${encodeURIComponent(deviceId)}/display`);
  }

  /** `GET /api/v1/devices/{id}/capabilities` (spec §19) */
  public async getCapabilities(deviceId: string): Promise<WallpaperCapabilities> {
    return this.request<WallpaperCapabilities>("GET", `/devices/${encodeURIComponent(deviceId)}/capabilities`);
  }

  /** `POST /api/v1/wallpaper/prepare` — crops the exported PNG to the phone screen (spec §10 / §11). */
  public async prepareWallpaper(
    deviceId: string,
    path: string,
    width?: number,
    height?: number,
    mode?: CropMode
  ): Promise<PreparedWallpaper> {
    return this.request<PreparedWallpaper>("POST", "/wallpaper/prepare", {
      deviceId,
      path,
      width,
      height,
      mode,
    });
  }

  /** `POST /api/v1/wallpaper/send` — pushes the prepared image to the phone gallery. */
  public async sendWallpaper(deviceId: string, imagePath: string): Promise<WallpaperResult> {
    return this.request<WallpaperResult>("POST", "/wallpaper/send", { deviceId, imagePath });
  }

  /** `POST /api/v1/wallpaper/set-lock` (spec §17) */
  public async setLockWallpaper(deviceId: string, imagePath: string): Promise<WallpaperResult> {
    return this.request<WallpaperResult>("POST", "/wallpaper/set-lock", { deviceId, imagePath });
  }

  /** Spec §22. Opens the event stream. Returns a disposer that closes the socket. */
  public connectEvents(
    onEvent: (event: BridgeEvent) => void,
    onStatusChange?: (connected: boolean) => void
  ): () => void {
    this.disconnectEvents();

    // Browsers and UXP cannot set headers on a WebSocket handshake, so the token rides in the query.
    const url = this.token
      ? `${this.webSocketUrl}?token=${encodeURIComponent(this.token)}`
      : this.webSocketUrl;

    const socket = new WebSocket(url);
    this.socket = socket;

    socket.onopen = () => onStatusChange?.(true);
    socket.onclose = () => onStatusChange?.(false);
    socket.onerror = () => onStatusChange?.(false);
    socket.onmessage = (message: MessageEvent) => {
      try {
        onEvent(JSON.parse(String(message.data)) as BridgeEvent);
      } catch {
        // A malformed frame should not tear down the stream.
      }
    };

    return () => this.disconnectEvents();
  }

  public disconnectEvents(): void {
    if (this.socket) {
      this.socket.onopen = null;
      this.socket.onclose = null;
      this.socket.onerror = null;
      this.socket.onmessage = null;
      this.socket.close();
      this.socket = null;
    }
  }

  private async request<T>(method: string, path: string, body?: unknown): Promise<T> {
    const headers: Record<string, string> = { Accept: "application/json" };
    if (body !== undefined) {
      headers["Content-Type"] = "application/json";
    }
    if (this.token) {
      headers["X-PSMW-Token"] = this.token;
    }

    let response: Response;
    try {
      response = await fetch(`${this.baseUrl}${API_PREFIX}${path}`, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body),
      });
    } catch (error) {
      throw new BridgeError(
        "TRANSPORT_ERROR",
        `无法连接 PhoneBridge（${this.baseUrl}）。请确认服务已启动。`,
        0
      );
    }

    if (response.ok) {
      return (await response.json()) as T;
    }

    throw await this.toError(response);
  }

  private async toError(response: Response): Promise<BridgeError> {
    if (response.status === 401) {
      return new BridgeError(
        "PERMISSION_DENIED",
        "本地认证失败。请在下方填入 PhoneBridge 的 auth.token（位于 %AppData%\\PSMobileWallpaper\\auth.token）。",
        401
      );
    }

    try {
      const payload = (await response.json()) as ApiError;

      return new BridgeError(
        payload.errorCode ?? "UNKNOWN_ERROR",
        payload.message ?? `请求失败（HTTP ${response.status}）`,
        response.status
      );
    } catch {
      return new BridgeError("UNKNOWN_ERROR", `请求失败（HTTP ${response.status}）`, response.status);
    }
  }
}
