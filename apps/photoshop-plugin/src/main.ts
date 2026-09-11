import { PanelController } from "./components/panel";

/**
 * Panel entry point (spec §4.1). UXP loads this bundle when the panel is created, so the
 * controller is started immediately and owns the panel's lifetime.
 */
let controller: PanelController | null = null;

function boot(): void {
  try {
    controller = new PanelController();
    void controller.start();
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    const target = document.getElementById("message");

    if (target) {
      target.textContent = `面板初始化失败：${message}`;
      target.className = "message message--error";
    }
  }
}

if (document.readyState === "loading") {
  document.addEventListener("DOMContentLoaded", boot);
} else {
  boot();
}

// Photoshop destroys and recreates panel content when the workspace changes, so sockets are released here.
window.addEventListener("unload", () => controller?.dispose());
