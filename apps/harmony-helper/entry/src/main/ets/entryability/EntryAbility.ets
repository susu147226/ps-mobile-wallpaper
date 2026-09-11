import { AbilityConstant, ConfigurationConstant, UIAbility, Want } from '@kit.AbilityKit';
import { hilog } from '@kit.PerformanceAnalysisKit';
import { window } from '@kit.ArkUI';
import { BusinessError } from '@kit.BasicServicesKit';

const DOMAIN = 0x0000;
const TAG = 'PSMW';

/**
 * Shell for the wallpaper helper page.
 *
 * All the work happens in pages/Index so the page can hold the fetched image and drive the
 * SaveButton, which is a UI security component: the platform only grants the temporary write
 * permission while the user is clicking that specific button.
 */
export default class EntryAbility extends UIAbility {
  onCreate(want: Want, launchParam: AbilityConstant.LaunchParam): void {
    try {
      this.context.getApplicationContext().setColorMode(ConfigurationConstant.ColorMode.COLOR_MODE_DARK);
    } catch (err) {
      const e = err as BusinessError;
      hilog.warn(DOMAIN, TAG, 'setColorMode failed: %{public}s', e.message);
    }

    AppStorage.setOrCreate('psmwBridgeUrl', this.resolveBridgeUrl(want));
  }

  /**
   * The bridge address as seen from the device. `hdc rport tcp:18766 tcp:18765` forwards the
   * device's own port to the host, so this is loopback from the phone's point of view. The caller
   * can override it through a want parameter.
   */
  private resolveBridgeUrl(want: Want): string {
    const parameters = want.parameters ?? {};
    const fromWant = parameters['bridgeUrl'] as string | undefined;

    return fromWant ?? 'http://127.0.0.1:18766';
  }

  onDestroy(): void {
    hilog.info(DOMAIN, TAG, 'Ability onDestroy');
  }

  onWindowStageCreate(windowStage: window.WindowStage): void {
    windowStage.loadContent('pages/Index', (err) => {
      if (err.code) {
        hilog.error(DOMAIN, TAG, 'loadContent failed: %{public}s', JSON.stringify(err));
      }
    });
  }

  onWindowStageDestroy(): void {
    hilog.info(DOMAIN, TAG, 'Ability onWindowStageDestroy');
  }

  onForeground(): void {
    hilog.info(DOMAIN, TAG, 'Ability onForeground');
  }

  onBackground(): void {
    hilog.info(DOMAIN, TAG, 'Ability onBackground');
  }
}
