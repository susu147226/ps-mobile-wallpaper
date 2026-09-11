import { app, core } from "photoshop";
import { storage } from "uxp";
import type { OutputFormat, PhotoshopDocumentInfo } from "../models/types";

/** Spec §8: exported canvases land in a PSMobileWallpaper folder inside the plugin temp directory. */
const TEMP_FOLDER_NAME = "PSMobileWallpaper";

export class NoActiveDocumentError extends Error {
  constructor() {
    super("没有打开的 Photoshop 文档。请先打开或新建一个文档。");
    this.name = "NoActiveDocumentError";
  }
}

/** Spec §7. Reads the active document and maps it to the shared model. */
export function readActiveDocument(): PhotoshopDocumentInfo {
  const document = app.activeDocument;
  if (!document) {
    throw new NoActiveDocumentError();
  }

  return {
    name: document.name,
    width: Math.round(document.width),
    height: Math.round(document.height),
    resolution: document.resolution,
    colorMode: normalizeColorMode(document.mode),
  };
}

/**
 * Spec §8. Exports the canvas as PNG or JPEG into the temp workspace and returns the file's
 * native path, which the bridge then reads directly (both processes share the filesystem).
 */
export async function exportActiveDocument(format: OutputFormat): Promise<string> {
  const document = app.activeDocument;
  if (!document) {
    throw new NoActiveDocumentError();
  }

  const folder = await getExportFolder();
  const fileName = buildFileName(format);
  const file = await folder.createFile(fileName, { overwrite: true });

  // Photoshop only permits document mutation inside a modal execution scope.
  await core.executeAsModal(
    async () => {
      if (format === "jpg") {
        await document.saveAs.jpg(file, { quality: 12 }, true);
      } else {
        await document.saveAs.png(file, { compression: 6 }, true);
      }
    },
    { commandName: "PS Mobile Wallpaper: 导出画布" }
  );

  return file.nativePath;
}

async function getExportFolder(): Promise<storage.Folder> {
  const temp = await storage.localFileSystem.getTemporaryFolder();

  try {
    return await temp.getEntry(TEMP_FOLDER_NAME) as storage.Folder;
  } catch {
    return await temp.createFolder(TEMP_FOLDER_NAME);
  }
}

/** Spec §8: wallpaper_{timestamp}.png */
function buildFileName(format: OutputFormat): string {
  const now = new Date();
  const pad = (value: number, width = 2): string => String(value).padStart(width, "0");

  const timestamp =
    `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}` +
    `_${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;

  return `wallpaper_${timestamp}.${format}`;
}

/** Photoshop reports the mode as an enum name; the UI wants something short and readable. */
function normalizeColorMode(mode: string): string {
  const normalized = String(mode ?? "").toUpperCase();

  const map: Record<string, string> = {
    RGBCOLORENUM: "RGB",
    CMYKCOLORENUM: "CMYK",
    GRAYSCALECOLORENUM: "灰度",
    LABCOLORENUM: "Lab",
    BITMAPCOLORENUM: "位图",
    DUOTONECOLORENUM: "双色调",
    INDEXEDCOLORENUM: "索引",
  };

  return map[normalized] ?? String(mode ?? "");
}
