# CoverCropper

ホームページの表紙（カバー）画像を作るための Windows 用の簡易画像切り抜きツールです。

画像を読み込むと比率を保ったまま自動でリサイズし、**1024×768 px** の枠を好きな位置に動かして切り抜き、**JPEG（品質85）** で登録フォルダに保存します。

## 主な機能

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
dotnet publish CoverCropper/CoverCropper.csproj -c Release -r win-x64 --self-contained false
```

## フォルダ構成

| パス | 内容 |
|---|---|
| `CoverCropper/` | アプリ本体（WPF / MVVM） |
| `CoverCropper/doc/` | 設計書 |
| `CoverCropper.Tests/` | 単体テスト（xUnit） |
| `tools/IconGenerator/` | アプリアイコンの生成ツール |

## 設定とログ

- 設定: `%APPDATA%\CoverCropper\settings.json`（保存先フォルダ、ウィンドウ位置など）
- ログ: `%APPDATA%\CoverCropper\logs\app.log`
