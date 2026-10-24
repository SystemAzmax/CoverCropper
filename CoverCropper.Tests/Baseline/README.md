# 基準ファイル（Baseline）

CoverCropper の画像処理を共通エンジン（WebDeza.Imaging）へ置き換える**前**の処理結果です。置き換えた後も CoverCropper の出力が変わらないことを、`BaselineRegressionTests` がこれと比べて確かめます（webDeza 設計書 5.5-6・R05）。

- 作成日: 2026-10-24
- 作成元: コミット `6702527` の時点の CoverCropper（`ImageLoader` / `ImageResizer` / `ImageCropper` / `JpegSaver`）
- 作成方法（リポジトリ直下で）:

  ```
  dotnet run --project tools/BaselineGenerator -c Release -- CoverCropper.Tests/Baseline
  ```

**置き換えた後に作り直さないでください。** 作り直すと、新しい実装の結果を基準にしてしまい、比べる意味が無くなります（ツールは既にあるときは `--force` が無いと止まります）。

## 中身

| フォルダ | 内容 |
|---|---|
| `inputs/` | 入力画像。決まった模様をツールで作ったもの |
| `expected/{名前}.loaded.png` | `ImageLoader.Load` の結果（向きの反映・白背景合成・Bgr24） |
| `expected/{名前}.resized.png` | `ImageResizer` でリサイズした結果（全体） |
| `expected/{名前}.output.jpg` | 中央で 1024×768 に切り抜き、`JpegSaver` で品質 85 で保存した結果 |

| 名前 | 入力 | 確かめること |
|---|---|---|
| landscape | 3000×1000 PNG（72 DPI） | 横長の縮小（縦 768 基準）、DPI の無視 |
| portrait | 1500×2000 PNG | 縦長の縮小（横 1024 基準） |
| ratio4x3 | 2048×1536 PNG（300 DPI） | 4:3 の縮小 |
| small | 800×600 PNG | 拡大 |
| wide_small | 500×100 PNG | 大きな拡大（3840×768） |
| transparent | 1600×1200 PNG（透明・半透明・不透明） | 白背景での合成 |
| orientation6 | 1600×1200 TIFF（Orientation = 6） | 回転情報の反映 |
| palette | 1600×900 GIF（216 色のパレット） | パレット画像の変換 |
| gray16 | 1000×1500 PNG（16 bit グレースケール） | 16 bit から 8 bit への変換 |

## 入力に JPEG を使っていない理由

JPEG の展開結果は Windows の JPEG コーデックによって変わりうるため、入力は可逆の形式（PNG / TIFF / GIF）だけにしています。JPEG の EXIF 回転は既存の単体テストで確かめています。

同じ理由で、JPEG 出力の比較は次の 2 段にしています。

1. `JpegSaver` の出力が、この環境で同じ画素を「品質 85・メタデータなし」でエンコードした参照とバイト単位で一致すること（必ず確かめる）
2. 参照が `expected/{名前}.output.jpg` と一致する環境（基準を作った環境と同じコーデック）では、保存してある JPEG ともバイト単位で一致すること（一致しない環境では省略し、テストの出力に記録する）
