/** Spec §9 / §33 display helpers. */

export function formatSize(width: number, height: number): string {
  if (width <= 0 || height <= 0) {
    return "—";
  }

  return `${width} × ${height} px`;
}

export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) {
    return "0 B";
  }

  const units = ["B", "KB", "MB", "GB"];
  const exponent = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
  const value = bytes / Math.pow(1024, exponent);

  return `${value.toFixed(exponent === 0 ? 0 : 1)} ${units[exponent]}`;
}

/** Spec §33: a fixed-width bar, e.g. ████████░░░░░░░░ 68% */
export function formatProgressBar(percent: number, width = 16): string {
  const clamped = Math.max(0, Math.min(100, Math.round(percent)));
  const filled = Math.round((clamped / 100) * width);

  return "█".repeat(filled) + "░".repeat(Math.max(0, width - filled));
}
