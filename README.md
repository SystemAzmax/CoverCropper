# webDeza と CoverCropper

Windows 用の画像ツールを 2 つ収めたリポジトリです。2 つのアプリは、同じ画像処理エンジン（`WebDeza.Imaging`）を使います。

| アプリ | 内容 |
|---|---|
| [webDeza](#webdeza)（ウェブデザ） | Web 用の画像を編集するソフト。全体や選択範囲へのフィルター・切り抜き・回転などをして、ファイルサイズと画質を確かめながら保存する |
| [CoverCropper](#covercropper) | ホームページの表紙（カバー）画像を作る、簡易画像切り抜きツール。1024×768 px・JPEG に固定 |

どちらも、元の画像ファイルは読み込むだけで、変更・上書きしません。外部とも通信しません。

## webDeza

画像を開き、画像の全体か、矩形で選択した範囲に、ぼかしなどの処理をかけます。仕上げた画像は、保存のダイアログで**保存したときのファイルサイズと、圧縮後の見た目を確かめながら**、形式と画質を決めて保存します。

### 主な機能

- 画像を開く: 「開く」（Ctrl+O。複数選択可）、ドラッグ＆ドロップ（ファイル・フォルダ）、コマンドライン引数
  - 対応形式: JPEG / PNG / BMP / TIFF / GIF / JPEG XR / ICO / WebP / HEIF / AVIF（WebP・HEIF・AVIF は、Windows に拡張機能が入っている場合）
  - EXIF の向きを反映し、透過は保ったまま扱う（キャンバスでは市松模様の上に表示）
- 複数の画像を、画像ごとの子ウィンドウで並べる（同時に 50 枚まで）。重ねて表示・並べて表示・最大化、Ctrl+Tab で切り替え
- ズーム（ホイール・Ctrl++ / Ctrl+-・全体表示 / 100%）とパン（手のひら・中ボタン・Space+ドラッグ）。100% は画面の 1 物理ピクセルが画像の 1 px
- 矩形の選択範囲: ドラッグ・ハンドル・矢印キー・数値の入力。比率（自由 / 元画像と同じ / 1:1 / 4:3 / 3:2 / 16:9 / カスタム）と縦横の入れ替え
- 編集
  - 選択範囲で切り抜き（Ctrl+Shift+X）
  - 右・左に 90° 回転、左右反転・上下反転、画像サイズの変更
  - フィルター（ぼかし・基本補正（明るさ・コントラスト・彩度））を、選択範囲か画像全体にかける。値を変えている間はプレビューを表示し、［適用］で確定する
  - 元に戻す / やり直し（Ctrl+Z / Ctrl+Y。画像ごとに 50 段まで）
- 保存（Ctrl+E）: 保存のダイアログで形式とオプションを選ぶ
  - 保存できる形式: JPEG（品質）/ PNG（インターレース）/ BMP / TIFF（圧縮）
  - 設定を変えるたびに、保存したときのファイルサイズを表示する（表示したサイズと、保存したファイルのサイズは一致する）
  - 圧縮前と圧縮後を並べて比較できる（ズーム・パンは左右で連動）
  - JPEG はファイルサイズの上限（KB）を指定でき、上限に収まるいちばん高い品質を探して使う
  - 透過を保てない形式では、透過部分を背景色で塗る。メタデータ（撮影日時・位置情報など）は書き込まない
  - ファイル名は `元ファイル名_yyyyMMdd_HHmmss.拡張子` で、出力先フォルダに保存する。同名がある場合は `_2`, `_3` … を付け、既存のファイルは上書きしない
- すべて保存（Ctrl+Shift+E）: 開いているすべての画像を、同じ設定で 1 件ずつ保存する（進捗の表示と中止）

減色（GIF・PNG8 など 256 色以下への変換）は行いません。

形式・フィルター・道具は、`WebDeza/Composition/AppModules.cs` に登録を 1 行足すだけで、画面のコードを変えずに増やせます（設計書 1.3）。

## CoverCropper

ホームページの表紙（カバー）画像を作るための Windows 用の簡易画像切り抜きツールです。

画像を読み込むと比率を保ったまま自動でリサイズし、**1024×768 px** の枠を好きな位置に動かして切り抜き、**JPEG（品質85）** で登録フォルダに保存します。

### 主な機能

- ドラッグ＆ドロップ / 「開く」（Ctrl+O）で画像を読み込み
  - 対応形式: JPG / PNG / BMP / GIF / TIFF / WebP（WebP は OS のコーデックがある場合）
  - EXIF の回転情報を反映、透過部分は白で合成
- 比率維持リサイズ（横1024基準。縦が768未満になる場合は縦768基準）
- 切り抜き枠の移動: マウスドラッグ、矢印キー（1px）、Shift+矢印キー（10px）、Home（中央に戻す）
- 保存（Ctrl+S）: `元ファイル名_yyyyMMdd_HHmmss.jpg`。同名がある場合は `_2`, `_3` … を付け、既存ファイルは上書きしない
- 元画像ファイルは読み込むだけで、変更・上書きしない

## 動作環境

- Windows 10 / 11（64bit）
- .NET 10 デスクトップランタイム

## ビルドとテスト

```
dotnet build CoverCropper.slnx
dotnet test CoverCropper.slnx
```

配布用ビルド:

```
dotnet publish WebDeza/WebDeza.csproj -c Release -r win-x64 --self-contained false
dotnet publish CoverCropper/CoverCropper.csproj -c Release -r win-x64 --self-contained false
```

GitHub Actions の CI（`.github/workflows/ci.yml`）は、main へのプッシュとプルリクエストで、ビルド・テスト・配布用ビルドを行い、成果物 `webDeza-win-x64`・`CoverCropper-win-x64`・`test-results` を残します。

### UI テスト

`WebDeza.UiTests` は、ビルドした 2 つのアプリの exe を起動し、マウスとキーボードを実際に動かして画面を操作するテストです（FlaUI）。PC を使っている間は実行できないので、環境変数 `WEBDEZA_UITEST=1` があるときだけ実行し、無いとき（普段の `dotnet test` と CI）はスキップされます。

```powershell
dotnet build CoverCropper.slnx -c Release
$env:WEBDEZA_UITEST = "1"
dotnet test WebDeza.UiTests -c Release --no-build
dotnet test WebDeza.UiTests -c Release --no-build --filter No=T04   # 観点を 1 つだけ
```

- webDeza と CoverCropper は閉じておきます。2 つのアプリの設定（`settings.json`）はテストの前に退避し、終わったら戻します。
- テスト画像は初回に `%TEMP%\webDeza-uitest\images` に作ります（数分かかります）。画面のキャプチャは `%TEMP%\webDeza-uitest\shots` に残ります。
- マウスは、画面の端から 200 px 以内には動かしません（モニターどうしの境は端と見なさず、モニターの無い所との境は端と見なします）。

## フォルダ構成

| パス | 内容 |
|---|---|
| `WebDeza/` | webDeza 本体（WPF / MVVM） |
| `WebDeza/doc/` | webDeza の設計書・実装計画書・手動テスト手順 |
| `WebDeza.Tests/` | webDeza と WebDeza.Core の単体テスト（xUnit） |
| `WebDeza.UiTests/` | webDeza と CoverCropper の UI テスト（xUnit / FlaUI。exe を起動して画面を操作する） |
| `WebDeza.Imaging/` | 画像処理エンジン（画像データ・編集処理・フィルター・形式の仕組み・保存。WPF に依存しない） |
| `WebDeza.Imaging.Wic/` | 標準の形式プラグイン（WIC で画像ファイルを読み書きする） |
| `WebDeza.Imaging.Tests/` | エンジンと標準の形式プラグインの単体テスト（xUnit） |
| `WebDeza.Core/` | アプリ共通の部品（設定・ログ・出力ファイル名など。2 つのアプリで使う） |
| `CoverCropper/` | CoverCropper 本体（WPF / MVVM） |
| `CoverCropper/doc/` | CoverCropper の設計書 |
| `CoverCropper.Tests/` | CoverCropper の単体テスト（xUnit） |
| `tools/IconGenerator/` | アプリアイコンの生成ツール |
| `tools/BaselineGenerator/` | CoverCropper の画像処理を共通エンジンへ置き換える前の基準ファイル（`CoverCropper.Tests/Baseline`）を作るツール |
| `.github/workflows/` | GitHub Actions の CI |

## 設定とログ

2 つのアプリの設定とログは別の場所にあり、互いに読み書きしません。

| アプリ | 設定 | ログ |
|---|---|---|
| webDeza | `%APPDATA%\webDeza\settings.json`（出力先フォルダ、保存の設定、選択範囲の比率、ウィンドウの位置など） | `%APPDATA%\webDeza\logs\app.log` |
| CoverCropper | `%APPDATA%\CoverCropper\settings.json`（保存先フォルダ、ウィンドウ位置など） | `%APPDATA%\CoverCropper\logs\app.log` |
