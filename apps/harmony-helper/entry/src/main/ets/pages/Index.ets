import { common } from '@kit.AbilityKit';
import { http } from '@kit.NetworkKit';
import { photoAccessHelper } from '@kit.MediaLibraryKit';
import { fileIo as fs } from '@kit.CoreFileKit';
import { image } from '@kit.ImageKit';
import { hilog } from '@kit.PerformanceAnalysisKit';
import { BusinessError } from '@kit.BasicServicesKit';

const DOMAIN = 0x0000;
const TAG = 'PSMW';

const IMAGE_URL_SUFFIX = '/api/v1/wallpaper/latest-image';
const SAVE_FILE_NAME = 'psmw-wallpaper.png';

@Entry
@Component
struct Index {
  @State preview: PixelMap | undefined = undefined;
  @State status: string = '正在从 Photoshop 获取图片…';
  @State ready: boolean = false;

  private payload: ArrayBuffer | undefined = undefined;

  aboutToAppear(): void {
    this.fetchImage();
  }

  /**
   * Downloads the wallpaper the bridge has prepared.
   *
   * The bridge is reached over `hdc rport tcp:18766 tcp:18765`, so from the phone this is a
   * loopback call. There is no file-transfer route: hdc cannot write into the media library, and
   * the app cannot read the places hdc can write.
   */
  private async fetchImage(): Promise<void> {
    const baseUrl = AppStorage.get<string>('psmwBridgeUrl') ?? 'http://127.0.0.1:18766';
    const url = `${baseUrl}${IMAGE_URL_SUFFIX}`;

    const client = http.createHttp();

    try {
      const response = await client.request(url, {
        method: http.RequestMethod.GET,
        expectDataType: http.HttpDataType.ARRAY_BUFFER,
        connectTimeout: 10000,
        readTimeout: 30000,
      });

      if (response.responseCode !== 200) {
        this.status = `获取失败：PhoneBridge 返回 ${response.responseCode}`;
        return;
      }

      this.payload = response.result as ArrayBuffer;

      const source = image.createImageSource(this.payload);
      this.preview = await source.createPixelMap();

      this.ready = true;
      this.status = '已获取图片。点下面的按钮保存到相册。';
      hilog.info(DOMAIN, TAG, 'image fetched, %{public}d bytes', this.payload.byteLength);
    } catch (err) {
      const e = err as BusinessError;
      this.status = `无法连接 PhoneBridge：${e.message}`;
      hilog.error(DOMAIN, TAG, 'fetch failed: code=%{public}d msg=%{public}s', e.code, e.message);
    } finally {
      client.destroy();
    }
  }

  /**
   * Writes the already-fetched bytes into the gallery.
   *
   * Called only from SaveButton's onClick: that security component is what grants the temporary
   * write authorisation, so no media permission is declared anywhere in this app.
   */
  private async saveToGallery(): Promise<void> {
    if (!this.payload) {
      return;
    }

    const context = getContext(this) as common.UIAbilityContext;
    const helper = photoAccessHelper.getPhotoAccessHelper(context);

    try {
      const uri = await helper.createAsset(photoAccessHelper.PhotoType.IMAGE, 'png', {
        title: SAVE_FILE_NAME.replace('.png', ''),
      });

      const file = await fs.open(uri, fs.OpenMode.READ_WRITE | fs.OpenMode.CREATE);
      await fs.write(file.fd, this.payload);
      await fs.close(file.fd);

      this.status = '已保存到相册。请在「相册」中找到它，用「更多 → 设为壁纸」设置锁屏或桌面。';
      hilog.info(DOMAIN, TAG, 'saved to gallery: %{public}s', uri);
    } catch (err) {
      const e = err as BusinessError;
      this.status = `保存失败：${e.message}`;
      hilog.error(DOMAIN, TAG, 'save failed: code=%{public}d msg=%{public}s', e.code, e.message);
    }
  }

  build() {
    Column({ space: 16 }) {
      Text('PS Mobile Wallpaper')
        .fontSize(20)
        .fontWeight(FontWeight.Bold)
        .fontColor('#FFFFFF')

      if (this.preview) {
        Image(this.preview)
          .objectFit(ImageFit.Contain)
          .width('70%')
          .layoutWeight(1)
          .borderRadius(8)
      } else {
        Column().layoutWeight(1)
      }

      Text(this.status)
        .fontSize(13)
        .fontColor('#BBBBBB')
        .textAlign(TextAlign.Center)
        .padding({ left: 16, right: 16 })

      if (this.ready) {
        // A security component: the platform grants the gallery-write authorisation only for the
        // duration of this click, which is why the app needs no media permission.
        SaveButton({ icon: SaveIconStyle.FULL_FILLED, text: SaveDescription.SAVE_IMAGE })
          .onClick((event: ClickEvent, result: SaveButtonOnClickResult) => {
            if (result === SaveButtonOnClickResult.SUCCESS) {
              this.saveToGallery();
            } else {
              this.status = '保存未获授权。';
            }
          })
      }
    }
    .width('100%')
    .height('100%')
    .padding(20)
    .backgroundColor('#1E1E1E')
    .justifyContent(FlexAlign.Center)
  }
}
