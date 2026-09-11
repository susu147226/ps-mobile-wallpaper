/**
 * Minimal ambient declarations for the Photoshop UXP API surface this panel uses.
 *
 * Adobe does not publish a maintained Photoshop UXP typings package on npm, so the subset below is
 * declared locally instead of pulling in an unverified third-party package. Extend this file when a
 * new Photoshop API is used.
 */
declare module "photoshop" {
  export interface DocumentInfo {
    readonly name: string;
    readonly width: number;
    readonly height: number;
    readonly resolution: number;
    readonly mode: string;
  }

  export interface PngSaveOptions {
    compression?: number;
  }

  export interface JpegSaveOptions {
    quality?: number;
  }

  export interface SaveAs {
    png(entry: unknown, options?: PngSaveOptions, asCopy?: boolean): Promise<void>;
    jpg(entry: unknown, options?: JpegSaveOptions, asCopy?: boolean): Promise<void>;
  }

  export interface Document extends DocumentInfo {
    readonly id: number;
    readonly saveAs: SaveAs;
  }

  export interface PhotoshopApp {
    readonly activeDocument: Document | null;
    readonly documents: Document[];
  }

  export const app: PhotoshopApp;

  export interface ExecuteAsModalOptions {
    commandName: string;
  }

  export namespace core {
    function executeAsModal<T>(
      targetFunction: (executionContext: unknown) => Promise<T>,
      options?: ExecuteAsModalOptions
    ): Promise<T>;
  }

  export namespace action {
    function batchPlay(commands: unknown[], options?: unknown): Promise<unknown>;
  }
}

declare module "uxp" {
  export namespace storage {
    export interface FileSystemProvider {
      getTemporaryFolder(): Promise<Folder>;
      getFolder(): Promise<Folder>;
      getFileForSaving(suggestedName?: string): Promise<File | null>;
      getFileForOpening(options?: { initialLocation?: string; types?: string[] }): Promise<File | null>;
    }

    export interface Entry {
      readonly name: string;
      readonly nativePath: string;
    }

    export interface File extends Entry {
      read(options?: { format?: string }): Promise<string | ArrayBuffer>;
      write(data: string | ArrayBuffer, options?: { format?: string }): Promise<void>;
    }

    export interface Folder extends Entry {
      createFile(name: string, options?: { overwrite?: boolean }): Promise<File>;
      createFolder(name: string): Promise<Folder>;
      getEntry(name: string): Promise<Entry>;
      getEntries(): Promise<Entry[]>;
    }

    export const localFileSystem: FileSystemProvider;
  }

  export namespace shell {
    function openPath(path: string): Promise<void>;
  }
}

declare module "os" {
  /** UXP's os shim exposes only the path helpers a plugin realistically needs. */
  export function homedir(): string;
  export function tmpdir(): string;
  export function platform(): string;

  const os: {
    homedir(): string;
    tmpdir(): string;
    platform(): string;
  };

  export default os;
}
