# 表紙画像切り抜きツール「CoverCropper」アプリケーション設計書

| 項目 | 内容 |
|---|---|
| 文書バージョン | 1.0 |
| 作成日 | 2026-10-13 |
| 対象 | Claude Code による実装 |
| 対象OS | Windows 10 / 11（64bit）のみ |
| 言語 / FW | C# / WPF / .NET 10 |

---

## 1. 概要

ホームページの表紙（カバー）用画像を作るための簡易画像編集アプリケーション。
画像を読み込むと、比率を維持したまま所定サイズへ自動リサイズし、その画像から **1024×768 px** の範囲をユーザーが任意の位置で切り抜き、**JPEG（品質85）** として事前に登録したフォルダへ保存する。

### 1.1 基本フロー

```
[画像をD&D or 開く]
      ↓
[リサイズ（比率維持）]  … 横1024基準。縦が768未満になるなら縦768基準
      ↓
[プレビュー表示 + 1024×768 の切り抜き枠]
      ↓  ユーザーが枠をドラッグ / 矢印キーで移動
[保存] → 登録フォルダに「元名_日時.jpg」（品質85）で出力
      ↓
（同じ画像で枠を動かして再保存可能 / 別画像を読み込み）
```

### 1.2 前提・制約

- 元画像ファイルは **絶対に上書き・変更しない**（読み込みのみ。リサイズ結果はメモリ上でのみ保持）。
- リサイズ後の画像そのものはファイル保存しない（保存するのは切り抜き結果のみ）。
- 外部サービスへの通信は一切行わない。

---

## 2. 機能要件

### F-01 画像の読み込み

| 項目 | 仕様 |
|---|---|
| 読み込み方法 | ① ウィンドウへのドラッグ＆ドロップ ② メニュー/ボタン「開く」（OpenFileDialog） ③ `Ctrl+O` |
| 対応形式 | `.jpg` `.jpeg` `.png` `.bmp` `.gif` `.tif` `.tiff` `.webp`（webp は OS の WIC コーデックがある場合のみ） |
| 複数ファイルD&D | 先頭の1ファイルのみ読み込み、ステータスバーに「最初の1件のみ読み込みました」と表示 |
| 非対応ファイル | エラーダイアログ「対応していないファイル形式です」を表示し、現在の状態を維持 |
| GIF | 1フレーム目のみ使用 |
| EXIF回転 | JPEG の EXIF Orientation を反映して正しい向きにしてから処理する |
| 透過 | アルファチャネルを持つ画像は **白背景で合成** する（JPEGは透過非対応のため） |
| カラーモード | CMYK・グレースケール・16bit 等は `Bgr24`（または `Bgra32`→白合成→`Bgr24`）に変換して扱う |
| DPI | 元画像のDPI情報は無視し、**ピクセル数のみ** を基準に処理する（96DPIに正規化） |
| ファイルロック | 読み込み時は `BitmapCacheOption.OnLoad` を使い、読み込み後に元ファイルのロックを解放する |
| 編集中の再読み込み | 既に画像表示中でも確認なしで新しい画像に置き換える（保存は都度確定しているため） |

### F-02 リサイズ（比率維持）

元画像サイズを `W × H` とする。

```
newW = 1024
newH = round(H × 1024 / W)

if newH < 768:
    newH = 768
    newW = max(1024, round(W × 768 / H))
```

| 例 | 元サイズ | 判定 | リサイズ後 |
|---|---|---|---|
| 縦長 | 3000×4000 | 横1024基準 | 1024×1365 |
| 4:3 | 4000×3000 | 横1024基準（ちょうど768） | 1024×768 |
| 横長 | 6000×2000 | 縦が341 < 768 → 縦768基準 | 2304×768 |
| 小さい画像 | 800×600 | 横1024基準（拡大） | 1024×768 |

- 拡大が必要な場合（`newW > W` または `newH > H`）も **そのまま拡大して処理** する。
  その際、ステータスバーに「元画像より拡大しています（画質が低下する可能性があります）」と警告を表示する（ダイアログは出さない）。
- リサイズの補間は高品質なものを使用する（WPF の `BitmapScalingMode.HighQuality`（Fant）相当。縮小率が大きい場合も品質を確保する）。
- 結果は必ず **幅 ≥ 1024 かつ 高さ ≥ 768** となる（切り抜き枠が必ず収まる）。

### F-03 プレビュー表示と切り抜き枠

| 項目 | 仕様 |
|---|---|
| 表示 | リサイズ後画像をウィンドウに **全体が収まるよう縮小表示**（Fit）。ウィンドウサイズ変更に追従 |
| 切り抜き枠 | リサイズ後画像上で **常に 1024×768 px 固定**。表示倍率に合わせて枠も同倍率で描画 |
| 枠外の表示 | 枠外を半透明の黒（例: `#80000000`）で暗くし、切り抜き範囲を明確にする |
| 枠の初期位置 | 画像の **中央** |
| 移動（マウス） | 枠内をドラッグで移動。カーソルは `SizeAll` |
| 移動（キー） | 矢印キーで 1px、`Shift`+矢印で 10px 移動（リサイズ後画像のピクセル単位） |
| 移動範囲 | 画像外にはみ出さないよう常にクランプ（`0 ≤ X ≤ newW-1024`, `0 ≤ Y ≤ newH-768`） |
| 移動軸 | リサイズ仕様上、片方の辺は枠と同じ長さになるため、実質的に **縦または横の一方向のみ移動** となる（両方とも一致する場合は移動不可） |
| 座標表示 | ステータスバーに `元: W×H → リサイズ後: newW×newH ／ 切り抜き位置: (X, Y)` を表示 |
| 位置リセット | ボタン「中央に戻す」で初期位置へ |

> 座標は表示座標ではなく **リサイズ後画像のピクセル座標（整数）** で保持し、表示時のみ倍率を掛ける。

### F-04 保存

| 項目 | 仕様 |
|---|---|
| 操作 | ボタン「保存」または `Ctrl+S` |
| 保存内容 | リサイズ後画像から現在の枠位置で **1024×768 px** を切り出したもの（`CroppedBitmap`） |
| 形式 | JPEG、品質 **85**（`JpegBitmapEncoder.QualityLevel = 85`） |
| 保存先 | 設定で登録済みのフォルダ（F-05） |
| ファイル名 | `{元ファイル名(拡張子なし)}_{yyyyMMdd_HHmmss}.jpg` 例: `photo_20261013_153012.jpg` |
| 名前重複 | 同名が既に存在する場合は `_2`, `_3` … を付与（例: `photo_20261013_153012_2.jpg`）。**既存ファイルは上書きしない** |
| 元画像の保護 | 保存先フルパスが元画像のフルパスと一致する場合は保存しない（安全装置。命名規則上通常は起こらない） |
| 保存先未設定 | 「保存先フォルダが未設定です」と表示し、フォルダ選択ダイアログを開く。選択されれば設定に登録して保存を続行 |
| 保存先が存在しない | 「保存先フォルダが見つかりません」エラー → 設定画面への誘導 |
| 保存後 | ステータスバーに「保存しました: ファイル名」を表示。画像と枠位置は **保持** し、枠を動かして続けて保存可能 |
| メタデータ | EXIF等のメタデータは書き込まない（位置情報漏えい防止） |
| 書き込み方式 | 一時ファイル（同フォルダの `*.tmp`）に書き込み後リネームし、途中失敗で壊れたJPEGを残さない |
| 補助操作 | ボタン「保存先フォルダを開く」（エクスプローラーで開く） |

### F-05 設定（保存先フォルダの登録）

| 項目 | 仕様 |
|---|---|
| 画面 | メニュー「設定」から開く設定ダイアログ（または メイン画面上部に保存先パスと「変更…」ボタンを常時表示） |
| 登録項目 | 保存先フォルダパス（`OpenFolderDialog` で選択。.NET 8 以降の WPF 標準） |
| 保存場所 | `%APPDATA%\CoverCropper\settings.json` |
| 形式 | `{ "OutputFolder": "C:\\...\\cover" }`（`System.Text.Json`） |
| 読み込み失敗 | JSON 破損時は既定値（未設定）で起動し、警告をステータスバーに表示 |
| 追加で保持（任意） | 前回のウィンドウサイズ・位置、前回「開く」で使ったフォルダ |

> 出力サイズ（1024×768）・JPEG品質（85）は固定値として定数定義する（設定画面には出さない）。将来変更しやすいよう `AppConstants` に集約する。

---

## 3. 画面設計

### 3.1 メインウィンドウ

```
┌───────────────────────────────────────────────────────────┐
│ CoverCropper                                        _ □ × │
├───────────────────────────────────────────────────────────┤
│ [開く] [保存] [中央に戻す]   保存先: D:\web\cover [変更…] [開く]│
├───────────────────────────────────────────────────────────┤
│                                                           │
│   ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░       │
│   ░░┌──────────────────────────────┐░░░░░░░░░░░░░░       │
│   ░░│                              │░░  ← 枠外は暗く      │
│   ░░│     1024×768 切り抜き枠       │░░                   │
│   ░░│   （ドラッグで移動）           │░░                   │
│   ░░└──────────────────────────────┘░░░░░░░░░░░░░░       │
│   ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░       │
│                                                           │
├───────────────────────────────────────────────────────────┤
│ 元: 6000×2000 → 2304×768 ／ 位置: (640, 0) ／ 保存しました: …│
└───────────────────────────────────────────────────────────┘
```

- 画像未読み込み時は中央に「ここに画像をドラッグ＆ドロップ、または［開く］」を表示。
- 未読み込み時は「保存」「中央に戻す」を無効化。
- D&D中（DragOver）は対応ファイルならドロップ領域をハイライト、非対応なら `DragDropEffects.None`。
- 最小ウィンドウサイズ: 800×600。初期サイズ: 1280×900。
- 高DPI対応: Per-Monitor V2（`app.manifest`）。

### 3.2 キーボードショートカット

| キー | 動作 |
|---|---|
| `Ctrl+O` | 開く |
| `Ctrl+S` | 保存 |
| `←↑→↓` | 枠を1px移動 |
| `Shift+←↑→↓` | 枠を10px移動 |
| `Home` | 枠を中央に戻す |

### 3.3 メッセージ一覧

| ID | 種別 | 文言 |
|---|---|---|
| M01 | エラー | 対応していないファイル形式です。 |
| M02 | エラー | 画像を読み込めませんでした。（{詳細}） |
| M03 | 警告(ステータス) | 元画像より拡大しています（画質が低下する可能性があります）。 |
| M04 | 情報(ステータス) | 保存しました: {ファイル名} |
| M05 | 確認 | 保存先フォルダが未設定です。フォルダを選択してください。 |
| M06 | エラー | 保存先フォルダが見つかりません: {パス} |
| M07 | エラー | 保存に失敗しました。（{詳細}） |
| M08 | 情報(ステータス) | 最初の1件のみ読み込みました。 |

---

## 4. 技術設計

### 4.1 技術スタック

| 項目 | 採用 |
|---|---|
| ランタイム | .NET 10（`net10.0-windows`） |
| UI | WPF（`<UseWPF>true</UseWPF>`） |
| パターン | MVVM（`CommunityToolkit.Mvvm` を使用可） |
| 画像処理 | WPF 標準（WIC: `BitmapDecoder`, `TransformedBitmap`/`DrawingVisual`+`RenderTargetBitmap`, `CroppedBitmap`, `JpegBitmapEncoder`）。**System.Drawing / 外部画像ライブラリは使用しない** |
| 設定 | `System.Text.Json` |
| テスト | xUnit（画像処理ロジック・ファイル名生成・設定の単体テスト） |
| 配布 | `dotnet publish -c Release -r win-x64 --self-contained false`（必要に応じ単一ファイル化） |

### 4.2 ソリューション構成

```
CoverCropper/
├─ CoverCropper.sln
├─ src/
│  └─ CoverCropper/
│     ├─ CoverCropper.csproj
│     ├─ app.manifest                  … PerMonitorV2
│     ├─ App.xaml / App.xaml.cs
│     ├─ AppConstants.cs               … 1024, 768, 85 などの定数
│     ├─ Views/
│     │  ├─ MainWindow.xaml(.cs)
│     │  ├─ SettingsWindow.xaml(.cs)
│     │  └─ Controls/
│     │     └─ CropCanvas.xaml(.cs)    … 画像表示＋枠描画＋ドラッグ処理
│     ├─ ViewModels/
│     │  ├─ MainViewModel.cs
│     │  └─ SettingsViewModel.cs
│     ├─ Services/
│     │  ├─ ImageLoader.cs             … 読み込み・EXIF回転・透過合成・Bgr24化
│     │  ├─ ImageResizer.cs            … サイズ計算・リサイズ
│     │  ├─ ImageCropper.cs            … 切り抜き・座標クランプ
│     │  ├─ JpegSaver.cs               … JPEG保存（品質85・一時ファイル経由）
│     │  ├─ OutputFileNamer.cs         … ファイル名生成・重複回避
│     │  └─ SettingsService.cs         … settings.json 読み書き
│     └─ Models/
│        ├─ AppSettings.cs
│        └─ CropState.cs               … 原寸/リサイズ後サイズ・枠位置
└─ tests/
   └─ CoverCropper.Tests/
      ├─ ImageResizerTests.cs
      ├─ ImageCropperTests.cs
      ├─ OutputFileNamerTests.cs
      └─ SettingsServiceTests.cs
```

### 4.3 主要クラス・インターフェース

```csharp
public static class AppConstants
{
    public const int TargetWidth  = 1024;
    public const int TargetHeight = 768;
    public const int JpegQuality  = 85;
    public static readonly string[] SupportedExtensions =
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp" };
}

public interface IImageLoader
{
    /// 元ファイルを読み取り専用で開き、EXIF回転反映・白背景合成済み・96DPIの Bgr24 BitmapSource を返す（Freeze済み）
    BitmapSource Load(string path);
}

public interface IImageResizer
{
    /// F-02 の計算式に従ったリサイズ後サイズを返す（純粋関数・単体テスト対象）
    (int Width, int Height) CalculateSize(int srcWidth, int srcHeight);
    BitmapSource Resize(BitmapSource source, int width, int height);
}

public interface IImageCropper
{
    /// 枠位置を画像内にクランプ（純粋関数）
    (int X, int Y) Clamp(int x, int y, int imageWidth, int imageHeight);
    (int X, int Y) Center(int imageWidth, int imageHeight);
    BitmapSource Crop(BitmapSource source, int x, int y); // 1024x768
}

public interface IJpegSaver
{
    void Save(BitmapSource image, string path, int quality);
}

public interface IOutputFileNamer
{
    /// 元名_yyyyMMdd_HHmmss.jpg、重複時は _2, _3...
    string CreatePath(string outputFolder, string sourcePath, DateTime now);
}

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
}
```

### 4.4 実装上の注意点

1. **元画像の保護**
   - `FileStream` は `FileMode.Open, FileAccess.Read, FileShare.Read` で開く。
   - `BitmapCacheOption.OnLoad` で読み込み、ストリームは即破棄。
   - 元パスへの書き込みコードは一切存在しないこと。保存時に `Path.GetFullPath` で元パスとの一致チェックを行う。
2. **DPI 問題の回避**
   - WPF の `BitmapSource` はDPIにより表示サイズ・`RenderTargetBitmap` の結果が変わるため、読み込み直後に 96DPI に正規化（`BitmapSource.Create` で再作成 等）し、以降は `PixelWidth/PixelHeight` のみを使う。
   - 切り抜き座標・サイズは必ずピクセル単位の整数で扱う。
3. **リサイズ品質**
   - `DrawingVisual` + `RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality)` + `RenderTargetBitmap(newW, newH, 96, 96, Pbgra32)` で描画し、最後に `FormatConvertedBitmap` で `Bgr24` へ変換、または `TransformedBitmap` を用いる。
   - 大きな縮小率（例: 1/4以下）でジャギーが出る場合は段階的縮小（1/2ずつ）を検討する。
4. **EXIF Orientation**
   - `BitmapMetadata.GetQuery("/app1/ifd/{ushort=274}")` で取得し、値 2〜8 に応じて `TransformedBitmap`（回転/反転）を適用する。
5. **透過合成**
   - `Format.Masks` / `PixelFormats` でアルファの有無を判定し、白で塗った `DrawingVisual` 上に描画してから `Bgr24` 化する。
6. **非同期処理**
   - 読み込み・リサイズ・保存は `Task.Run` でバックグラウンド実行し、UI をブロックしない。`BitmapSource` は `Freeze()` してスレッド間で受け渡す。
   - 処理中はカーソルを待機にし、操作ボタンを無効化する。
7. **大きな画像**
   - 1億画素級でも落ちないよう、`BitmapImage.DecodePixelWidth` 等によるデコード時縮小は **使わない**（品質優先）が、`OutOfMemoryException` は捕捉して M02 を表示する。
   - x64 ビルドとする。
8. **例外処理**
   - 全体の未処理例外は `DispatcherUnhandledException` で捕捉し、ログ（`%APPDATA%\CoverCropper\logs\app.log`）に出力してダイアログ表示。

### 4.5 処理シーケンス（読み込み〜保存）

```
User        MainWindow/VM        ImageLoader   ImageResizer   ImageCropper   JpegSaver
 |--Drop------>|                      |              |              |             |
 |             |--Load(path)--------->|              |              |             |
 |             |<--BitmapSource(src)--|              |              |             |
 |             |--CalculateSize/Resize-------------->|              |             |
 |             |<--BitmapSource(resized)-------------|              |             |
 |             |--Center()---------------------------------------->|             |
 |<--表示+枠---|                                                    |             |
 |--Drag------>|--Clamp()----------------------------------------->|             |
 |--Ctrl+S---->|--Crop(resized,x,y)------------------------------->|             |
 |             |--CreatePath() (OutputFileNamer)                    |             |
 |             |--Save(cropped, path, 85)---------------------------------------->|
 |<--保存完了--|                                                                  |
```

---

## 5. テスト設計

### 5.1 単体テスト（xUnit）

| No | 対象 | 入力 | 期待結果 |
|---|---|---|---|
| U01 | CalculateSize | 3000×4000 | 1024×1365 |
| U02 | CalculateSize | 4000×3000 | 1024×768 |
| U03 | CalculateSize | 6000×2000 | 2304×768 |
| U04 | CalculateSize | 800×600 | 1024×768（拡大） |
| U05 | CalculateSize | 500×100 | 3840×768（拡大） |
| U06 | CalculateSize | 1024×767 | 1025×768 以上かつ高さ768 |
| U07 | CalculateSize | 任意の乱数サイズ1000件 | 常に 幅≥1024 かつ 高さ≥768 |
| U08 | Clamp | 枠が負座標/右下はみ出し | 画像内に収まる |
| U09 | Center | 2304×768 | (640, 0) |
| U10 | Crop | 任意位置 | 出力が 1024×768 |
| U11 | OutputFileNamer | `C:\a\photo.png`, 2026-10-13 15:30:12 | `{out}\photo_20261013_153012.jpg` |
| U12 | OutputFileNamer | 同名既存あり | `_2` 付与、さらに既存なら `_3` |
| U13 | JpegSaver | 保存後に再読込 | JPEG形式・1024×768 |
| U14 | SettingsService | 破損JSON | 例外にならず既定値 |

### 5.2 結合・手動テスト

| No | 観点 | 確認内容 |
|---|---|---|
| T01 | 元画像保護 | 読み込み・保存後、元画像の更新日時・ハッシュが変化していない |
| T02 | ファイルロック | 読み込み中の元画像をエクスプローラーで削除/リネームできる |
| T03 | D&D | 対応/非対応/複数ファイル/フォルダのドロップ |
| T04 | EXIF | スマホ縦撮り写真が正しい向きで表示・保存される |
| T05 | 透過PNG | 透過部分が白で保存される |
| T06 | 枠操作 | ドラッグ・矢印キー・Shift+矢印で移動、はみ出さない |
| T07 | 連続保存 | 枠を動かして複数回保存でき、すべて別ファイルになる |
| T08 | 保存先未設定/削除済み | M05 / M06 が表示される |
| T09 | 高DPI | 150%/200% スケーリング環境で枠と保存結果がずれない |
| T10 | 大画像 | 8000×6000 程度で操作が実用的な速度で動く |
| T11 | 品質 | 保存JPEGの品質が85である（再エンコード比較 or ツールで確認） |

---

## 6. 受け入れ基準

- [ ] D&D または「開く」で画像を読み込める
- [ ] 比率維持で横1024にリサイズされ、縦が768未満になる場合は縦768基準でリサイズされる
- [ ] 元画像ファイルが一切変更されない
- [ ] 1024×768 の枠を任意位置に移動して切り抜ける
- [ ] 切り抜き結果が品質85のJPEGで、登録フォルダに `元名_日時.jpg` で保存される
- [ ] 同じ画像から複数回切り抜き保存できる
- [ ] 保存先フォルダが設定として永続化される
- [ ] 5章の単体テストがすべて成功する

---

## 7. 対象外（将来拡張候補）

- 出力サイズ・JPEG品質の変更UI
- 拡大縮小（ズーム）しながらの枠操作、枠サイズの可変化
- 複数画像の一括処理
- 回転・明るさ補正などの編集機能
- インストーラー作成

---

## 8. Claude Code への実装指示（要約）

1. 4.2 の構成で .NET 10 WPF ソリューションとテストプロジェクトを作成する。
2. まず `ImageResizer.CalculateSize` / `ImageCropper.Clamp` / `OutputFileNamer` を実装し、5.1 の単体テストを通す。
3. `ImageLoader`（EXIF・透過・DPI正規化）→ `JpegSaver` → `SettingsService` を実装する。
4. `CropCanvas` コントロールと `MainWindow` を MVVM で実装する（D&D、ドラッグ、キー操作、ステータスバー）。
5. `dotnet build` / `dotnet test` が成功することを確認する。
6. 元画像に書き込むコードパスが存在しないことをレビューで確認する。
