# 画像編集ソフト「webDeza」アプリケーション設計書

| 項目 | 内容 |
|---|---|
| 文書バージョン | 2.1 |
| 作成日 | 2026-10-24 |
| 対象 | Claude Code による実装 |
| 対象OS | Windows 10 / 11（64bit）のみ |
| 言語 / FW | C# / WPF / .NET 10 |
| 名称 | webDeza（ウェブデザ）。表記は先頭の w だけ小文字（5.9） |
| 関連文書 | `CoverCropper/doc/CoverCropper_設計書.md`（CoverCropper 設計書 1.0。以下「CoverCropper 設計書」） |

| 版 | 日付 | 内容 |
|---|---|---|
| 1.0 | 2026-10-24 | 初版（仮称 CoverCropper2。CoverCropper に 4 つの拡張を加えた別アプリ） |
| 2.0 | 2026-10-24 | 画像編集ソフトとして、入力形式・出力形式・編集機能を足しやすい構成に変更。CoverCropper を、共通の画像処理エンジンを固定の設定で使う簡易版と位置付けた |
| 2.1 | 2026-10-24 | 名称を webDeza に決定。書き出しサイズの表示・書き出しプレビュー・ファイルサイズの上限を追加。減色は行わないことを明記。CoverCropper の載せ替えを、ファイルを移さずに中身だけ置き換える方式に変更 |

---

## 1. 概要

webDeza は、販売を終えた「OPTPiX webDesigner」の代わりに使うことを目指す画像編集ソフトである。写真などの画像を読み込み、切り抜き・リサイズ・回転・色調補正などを加え、**ファイルサイズと画質を確かめながら**指定した形式で書き出す。入力形式・出力形式・編集機能（フィルター）・キャンバスの道具を、後から足しやすい構成にすることを最も重視する。

CoverCropper は、webDeza と同じ画像処理エンジンを「1024×768・JPEG 品質 85・表紙用の切り抜き」という固定の設定で使う簡易版とする。CoverCropper の画面・操作・出力結果は今のまま変えない。

### 1.1 2 つのアプリの位置付け

| 項目 | CoverCropper（簡易版） | webDeza（編集ソフト） |
|---|---|---|
| 目的 | ホームページの表紙画像をすばやく作る | Web 用の画像の編集と書き出し全般 |
| 画面 | 今の画面のまま | 新しい画面（一覧・キャンバス・編集パネル・書き出しプレビュー） |
| 入力形式 | 固定（JPG / PNG / BMP / GIF / TIFF / WebP） | 形式プラグインに登録されたものすべて |
| 出力 | JPEG・品質 85・1024×768 に固定 | 形式・形式ごとのオプション・サイズ・ファイルサイズの上限を選べる。プリセットあり |
| ファイルサイズの確認 | なし | 設定を変えるたびに書き出しサイズを表示。圧縮前後を並べて比べられる |
| 編集 | 枠の位置だけ | 切り抜き（比率指定・自由）、リサイズ、回転・反転、フィルター、元に戻す / やり直し |
| 枚数 | 1 枚ずつ | 複数の画像を一覧で扱い、一括で書き出せる |
| 画像処理 | 共通エンジン（固定の順番と値で呼ぶ） | 共通エンジン |

### 1.2 構成

```
  ┌──────────────────────────┐    ┌──────────────────────────────┐
  │ CoverCropper（簡易版）   │    │ webDeza（編集ソフト）        │
  │ 今の画面 ＋ 固定の設定   │    │ 新しい画面 ＋ 拡張の登録     │
  └────────────┬─────────────┘    └──────────────┬───────────────┘
               │                                 │
               ├──────────────┬──────────────────┤
               │              │                  │
               │   ┌──────────┴───────────────┐  │
               │   │ WebDeza.Imaging.Wic      │  │  標準の形式プラグイン（WIC）
               │   └──────────┬───────────────┘  │
               │              │                  │
  ┌────────────┴──────────────┴──────────────────┴──────────────┐
  │ WebDeza.Imaging（画像処理エンジン。WPF に依存しない）       │
  │ 画像データ・編集処理・フィルター・形式の仕組み・書き出し    │
  └─────────────────────────────────────────────────────────────┘
  ＋ WebDeza.Core（アプリ共通: 設定・ログ・出力ファイル名）
```

### 1.3 拡張のしかた

| 足したいもの | 実装するもの | 登録する場所 | 画面の変更 |
|---|---|---|---|
| 入力形式（例: カメラ RAW） | `IImageFormat` ＋ `IImageDecoder` | `FormatRegistry` | 不要。「開く」の種類の一覧と D&D の判定は登録内容から作る |
| 出力形式（例: WebP・AVIF の書き出し） | `IImageFormat` ＋ `IImageEncoder`（オプションは `ParameterDescriptor` で記述。サイズを調整できるパラメーターがあれば `SizeTuning` で宣言） | `FormatRegistry` | 不要。形式の一覧・オプションの入力欄・書き出しサイズの表示・ファイルサイズの上限は登録内容から作る |
| 編集機能（例: シャープ、モノクロ） | `IImageFilter`（パラメーターは `ParameterDescriptor` で記述） | `FilterRegistry` | 不要。編集パネルに自動で並ぶ。専用の画面が要るときだけ DataTemplate を足す |
| キャンバスの道具（例: 範囲選択、文字入れ） | `ICanvasTool` | `ToolRegistry` | 不要。道具のボタンは登録内容から作る |
| 書き出しプリセット | `ExportPreset` | 組み込みは `PresetCatalog`、利用者のものは設定ファイル | 不要 |

webDeza での登録は `WebDeza/Composition/AppModules.cs` の 1 か所にまとめる。外部ライブラリが要る形式（WebP や AVIF の書き出しなど）は、別のプラグインプロジェクトにして、ここで登録する。

### 1.4 前提・制約

- 元画像を上書き・変更しない。元ファイルを開くのは形式レジストリ（`FormatRegistry.Decode`）だけとし、読み取り専用で開いて、読み終えたらすぐ閉じる。
- 外部と通信しない。
- エンジン・標準プラグイン・アプリ本体では、System.Drawing と外部の画像ライブラリを使わない（WPF / WIC と自前のピクセル処理だけ）。外部ライブラリが要る形式は、別のプラグインプロジェクトに閉じ込める。
- **非破壊編集**とする。元画像と「編集内容（レシピ）」を分けて持ち、表示と書き出しのたびにレシピから作り直す。
- **減色（GIF・PNG8 などの 256 色以下への変換）は行わない**。今の回線速度では必要性が低いため（2026-10-24 の判断）。
- CoverCropper の画面・操作・出力結果・設定とログの場所を変えない。CoverCropper の既存テスト（53 件）を書き換えずに通す（5.5）。
- 一覧と編集内容はアプリを終了すると消える（編集内容のファイル保存は 8 章の将来拡張）。

---

## 2. 機能要件（webDeza）

### F-01 画像の読み込み

| 項目 | 仕様 |
|---|---|
| 読み込み方法 | ① ウィンドウへのドラッグ＆ドロップ（ファイル・フォルダ、複数可） ② 「開く」（OpenFileDialog、複数選択可） ③ `Ctrl+O` ④ コマンドライン引数（複数可） |
| 対応形式 | 形式レジストリに登録され、読み込みができる形式（標準は 5.4 の表）。「開く」の種類の一覧と D&D の判定は登録内容から作る |
| 形式の判定 | 拡張子で形式を選ぶ。標準プラグイン（WIC）は中身を見て判定するので、拡張子と中身が違っていても読める |
| OS のコーデックが要る形式 | WebP・HEIF・AVIF は、Windows に拡張機能が入っている場合だけ読める。読めないときは M01 に続けて M21（必要な拡張機能の名前）を出す |
| 複数ページ・複数フレーム | 1 枚目だけを使う（GIF は 1 フレーム目、TIFF は 1 ページ目、ICO はいちばん大きい画像） |
| 向き | EXIF などの向きの情報を読み込み時に反映し、正しい向きにする |
| 透過 | アルファを保つ（CoverCropper と違い、読み込み時に白で塗らない）。キャンバスでは市松模様の上に表示する。透過を保てない形式で書き出すときだけ背景色で塗る（F-08） |
| 画素の形式 | すべて 8 bit の Bgra32（ストレートアルファ）に変換して扱う。16 bit などは 8 bit に落とす |
| DPI・色空間 | DPI は無視してピクセル数だけを扱う。ICC プロファイルによる色空間の変換はしない（どちらも CoverCropper と同じ） |
| フォルダ | フォルダ直下の対応ファイルを追加する（サブフォルダは見ない） |
| 追加先・並び順 | 一覧の末尾に追加し、今回追加したうちの最初の画像を選んで表示する。1 回に追加する分は、フルパスの自然順（エクスプローラーと同じ `StrCmpLogicalW`）で並べる |
| 重複 | 一覧にある画像や、同時に追加する画像とフルパスが同じもの（大文字小文字は区別しない）は追加しない |
| 上限 | 一覧は 500 件まで。超えた分は追加せず M15 |
| 非対応ファイル | 対応ファイルと混ざっている場合は除外し、M09 で件数を示す。1 件も対応していない場合は M01（フォルダだけを指定して画像が無い場合は M14） |
| D&D 中の表示 | 対応ファイルかフォルダを 1 つでも含む場合はハイライトして `DragDropEffects.Copy`、それ以外は `None` |
| 元ファイルのサイズ | 一覧に追加するときに元ファイルのバイト数を覚えておく（書き出しサイズとの比率の表示に使う。F-09） |
| メモリ | フル解像度で持つのは表示中の 1 枚だけ。ほかの画像はサムネイルと編集内容だけを持ち、選んだときに読み直す |
| 大きすぎる画像 | 幅 × 高さ × 4 バイトが .NET の配列の上限（約 2 GB）を超える画像は M02 |
| 読み込みの失敗 | その画像の状態を「エラー」にし、M01 / M02 を出す。一覧からは外さない |

### F-02 画像一覧

| 項目 | 仕様 |
|---|---|
| 表示 | 左のパネルに、サムネイル・ファイル名・状態を縦に並べる。見出しに件数 |
| サムネイル | 長辺 160 px 程度。追加後にバックグラウンドで 1 件ずつ作る。形式プラグインのサムネイル用の読み込み（縮小デコード・埋め込みサムネイル）を使ってよい。向きを反映し、利用者の回転・反転も表示に反映する |
| 状態 | 未書き出し / 書き出し済み（回数） / エラー（理由をツールチップに出す） |
| 編集済みの印 | 書き出していない編集（5.6 の `HasUnexportedEdits`）がある画像に印を付ける |
| 選択 | クリック、または `PageUp` / `PageDown`。矢印キーは枠の移動に使うため、一覧の選択には使わない |
| 一覧から外す | ボタン / `Delete`。**ファイルは削除しない**。表示中の画像を外したら、次（無ければ前）の画像を表示する |
| 一覧をクリア | ボタン。書き出していない編集がある画像があれば M16 で確認する |
| 編集内容の保持 | 画像ごとにレシピと履歴（F-07）を持ち、一覧から外すかアプリを終了するまで保つ |

### F-03 キャンバス（表示・ズーム・パン・道具）

| 項目 | 仕様 |
|---|---|
| 表示の切り替え | 中央の表示を［編集］と［書き出しプレビュー］（F-09）で切り替える（`P`）。この表の内容は［編集］のときのもの |
| 道具 | キャンバス左の縦のツールバーで切り替える。標準は「切り抜き」（`C`・既定）と「手のひら」（`H`）。`Space` を押している間は一時的に手のひら |
| 倍率の基準 | 100% = 編集画像の 1 px が画面の 1 物理ピクセル（高 DPI 環境でも等倍） |
| 全体表示 | 画像全体が収まる倍率。ただし 100% を超えて拡大しない（CoverCropper と同じ） |
| 範囲 | 最小は「全体表示の倍率」と 10% の小さい方、最大は 800% |
| ズーム | マウスホイール（カーソル位置が中心）、`Ctrl++` / `Ctrl+-` とボタン［－］［＋］（中央が中心）。1 段階 1.25 倍 |
| 全体 / 等倍 | `Ctrl+0` / ［全体］、`Ctrl+1` / ［100%］ |
| パン | 手のひらでドラッグ、中ボタンでドラッグ、`Space`+ドラッグ。切り抜きの道具で枠の外側をドラッグしたときもパン |
| パンの範囲 | 画像が領域より小さい向きは中央に固定。大きい向きは、画像の端が領域の内側に入り込まない範囲 |
| ウィンドウサイズの変更 | 全体表示のときは全体表示し直す。ズーム中は倍率と表示の中心を保つ |
| 画像の切り替え・90° 回転 | 全体表示に戻す |
| 表示用画像 | 長辺が 4096 px を超える画像は、長辺 4096 px に縮小した表示用画像で表示する（書き出しには使わない） |
| 補間 | 表示用画像の 1 px が 2 物理ピクセル未満なら `HighQuality`、それ以上なら `NearestNeighbor` |
| 透過 | 市松模様（8 DIP 角の明るい灰と白）の上に描く |
| 切り抜きの表示 | 枠外の暗さ（`#80000000`）・三分割のガイド・枠線は、どの道具のときも描く。ハンドルは「切り抜き」のときだけ描く |
| 倍率の表示 | 右下に表示（例: `37%`） |

### F-04 切り抜き

| 項目 | 仕様 |
|---|---|
| 座標系 | 回転・反転を適用した後の元画像（フル解像度。以下「編集画像」）のピクセル座標（整数） |
| 比率 | 書き出しサイズが「幅×高さ」（F-08）のときは、書き出しの縦横比に固定する。それ以外のときは、自由 / 元画像と同じ / 1:1 / 4:3 / 3:2 / 16:9 から選ぶ（既定は自由）。［縦横を入れ替え］で 4:3 ↔ 3:4 などにする |
| 比率の範囲 | 比率の選択は全画像で共通（一括書き出しと相性がよいため）。比率が変わったら、すべての画像の枠を初期状態に戻す |
| 初期状態 | 比率に合う最大の枠を、編集画像の中央に置く（端数は切り捨て）。自由のときは画像全体（＝切り抜かない） |
| 最小 | 幅・高さとも 16 px 以上。「幅×高さ」のときは、幅を ceil(書き出し幅 ÷ 8) 以上にする（拡大を 8 倍までにする）。最大より大きくなる場合は最大に合わせる |
| 移動（マウス） | 枠の内側をドラッグ（`SizeAll`） |
| 移動（キー） | 矢印で書き出し画像の 1 px 分、`Shift`+矢印で 10 px 分。編集画像のピクセルでは max(1, round(n ÷ 書き出し倍率)) |
| 大きさ（マウス） | 比率が固定なら四隅のハンドル、自由なら四隅と四辺のハンドル。反対側の角（辺）を固定する。反対側を越えても裏返らず、最小で止まる |
| 大きさ（キー） | `+` / `-`（テンキー可、`Shift` の有無は問わない）で幅の 5% ずつ。中心を保ち、端からはみ出す場合は内側へずらす |
| 中央に戻す / 最大にする | ［中央に戻す］ / `Home`（大きさを保って中央へ）、［最大にする］ / `Ctrl+Home`（初期状態） |
| 移動範囲 | 常に編集画像の内側 |
| 拡大の警告 | 書き出し倍率が 1 を超えるときは、ステータスバーに M03 |

> 書き出し倍率 = 書き出し画像の幅 ÷ 枠の幅。書き出しサイズが「幅×高さ」で 1024×768、枠が初期状態なら、CoverCropper とほぼ同じ範囲が切り抜かれる（CoverCropper は全体を縮小してから切り抜き、webDeza は切り抜いてから縮小するので、ピクセル単位では一致しない）。

### F-05 回転・反転

| 項目 | 仕様 |
|---|---|
| 操作 | 右に 90° 回転（`Ctrl+R`）、左に 90° 回転（`Ctrl+Shift+R`）、左右反転、上下反転 |
| 保持 | 操作の組み合わせを `ImageOrientation`（回転角 0/90/180/270 と左右反転の有無）としてレシピに持つ（5.7） |
| 枠 | 90° 回転したら初期状態に戻す。反転したら大きさを保ち、位置を反転に合わせる（左右反転: X' = 画像の幅 − X − 枠の幅、上下反転: Y' = 画像の高さ − Y − 枠の高さ） |
| 表示 | 90° 回転したら全体表示に戻す |

### F-06 フィルター（色調補正など）

| 項目 | 仕様 |
|---|---|
| 編集パネル | フィルターレジストリに登録されたフィルターを、登録順に 1 つずつのセクションとして並べる。各セクションに［リセット］ |
| 入力欄 | フィルターのパラメーターの記述（`ParameterDescriptor`）から自動で作る（5.6）。スライダーはダブルクリックで既定値に戻す |
| 適用順 | 登録順。各フィルターは値が既定値（`IsIdentity`）なら何もしない |
| 処理位置 | 切り抜き・リサイズの後（5.8）。プレビューと同じく「縮小した後に補正」となり、見た目がそろう |
| 透過 | フィルターはアルファを変えない（標準フィルター。アルファを変えるフィルターを足す場合はそのフィルターの仕様で決める） |
| プレビュー | 表示用画像にかけて表示する。スライダーの操作中は 50 ms 程度に間引いて更新し、止まったら必ず最後の値で更新する |
| 全画像に適用 | ［フィルターを全画像に適用］で、表示中の画像のすべてのフィルターの値を一覧の全画像に写す（M13 で確認）。回転と枠は写さない |

標準フィルター「基本補正」（ID: `basic`）:

| パラメーター | 範囲・既定 | 計算 |
|---|---|---|
| 明るさ | −100〜+100・0 | ガンマ補正。γ = 2^(−明るさ ÷ 100)、v' = 255 × (v ÷ 255)^γ。黒（0）と白（255）は変わらない |
| コントラスト | −100〜+100・0 | v' = (v − 127.5) × k + 127.5、k = 2^(コントラスト ÷ 100) |
| 彩度 | −100〜+100・0 | Y = 0.299R + 0.587G + 0.114B、C' = Y + (C − Y) × (1 + 彩度 ÷ 100)（C は R・G・B のそれぞれ） |

- 明るさとコントラストは実数のまま続けて計算し、1 回だけ四捨五入（`MidpointRounding.AwayFromZero`）して 0〜255 に収めた 256 段の LUT にする。彩度は LUT を適用した後の値から計算し、同じように丸める。

### F-07 元に戻す・やり直し

| 項目 | 仕様 |
|---|---|
| 操作 | `Ctrl+Z` / ［元に戻す］、`Ctrl+Y` または `Ctrl+Shift+Z` / ［やり直し］ |
| 対象 | 画像ごとのレシピ（枠・回転・反転・フィルターの値）。書き出し設定と切り抜きの比率は対象外（全画像共通の設定のため） |
| 範囲 | 画像ごとに別の履歴を持つ。一覧で画像を切り替えると、その画像の履歴になる |
| 1 回の単位 | ドラッグを離したとき、スライダーを離したとき、ボタン 1 回。同じ種類のキー操作（矢印など）が 1 秒以内に続いたときは 1 回にまとめる |
| 上限 | 画像ごとに 100 段 |
| 比率との関係 | 元に戻した枠が今の比率に合わないときは、枠を初期状態にする |
| 書き出しとの関係 | 書き出しても履歴は消さない |

### F-08 書き出し設定

全画像で共通。編集パネルの先頭の「書き出し」セクションで設定する。

| 項目 | 仕様 |
|---|---|
| 形式 | 形式レジストリに登録され、書き出しができる形式（標準: JPEG / PNG / BMP / TIFF）。既定は JPEG |
| 形式のオプション | 形式ごとのオプションの記述から入力欄を作る（標準は 5.4）。形式を切り替えても、それぞれの形式のオプションの値は覚えておく |
| 品質の決め方 | 形式がサイズを調整できる場合（標準では JPEG）、「品質を指定」と「ファイルサイズの上限を指定」から選ぶ。上限は KB で入力する（1〜1,000,000）。動作は F-09 |
| サイズ | ① 幅×高さ（プリセット: 1024×768 / 1280×960 / 1280×720 / 1920×1080 / 1200×630 / 1080×1080、またはカスタム 16〜8192）。切り抜きの比率はこれに固定される ② 長辺（16〜8192、既定 1920）。枠の長辺がこの長さになるよう拡大・縮小する ③ 指定しない（切り抜いた大きさのまま） |
| 背景色 | 透過を保てない形式で書き出すとき、透過部分をこの色で塗る（既定は白）。色見本（白・黒・灰）と `#RRGGBB` の入力。透過を保てない形式を選んでいるときは M24 をパネルに表示する |
| メタデータ | 書き込まない（位置情報などの漏えいを防ぐ。CoverCropper と同じ） |
| 出力先 | フォルダのパス、［変更…］、［開く］（エクスプローラー）。CoverCropper の「保存先」と同じ扱い |
| ファイル名 | `{元のファイル名}_{yyyyMMdd_HHmmss}.{形式の拡張子}`。同名があれば `_2`, `_3` …（CoverCropper と同じ規則。拡張子だけ形式に合わせる） |
| プリセット | 選ぶと書き出し設定（形式・オプション・品質の決め方・サイズ・背景色）に反映する。［プリセットとして保存…］で今の設定に名前を付けて保存し、利用者のプリセットは削除できる（M23）。組み込みのプリセットは下の表 |
| 縦横比が変わったとき | 書き出しの縦横比が変わって切り抜きの比率が変わる場合は、すべての画像の枠を初期状態に戻す |
| 永続化 | 今の書き出し設定・切り抜きの比率・利用者のプリセットを設定ファイルに保存する（F-12） |

組み込みのプリセット:

| 名前 | 形式 | 品質の決め方 | サイズ |
|---|---|---|---|
| 表紙画像（CoverCropper と同じ） | JPEG | 品質 85 | 幅×高さ 1024×768 |
| Web 用（長辺 1920） | JPEG | 品質 85 | 長辺 1920 |
| Web 用（長辺 1920・300 KB 以下） | JPEG | 上限 300 KB | 長辺 1920 |
| OGP（1200×630） | JPEG | 品質 85 | 幅×高さ 1200×630 |
| 正方形（1080×1080） | JPEG | 品質 90 | 幅×高さ 1080×1080 |
| PNG（大きさそのまま） | PNG | — | 指定しない |

初回起動時の書き出し設定は「表紙画像（CoverCropper と同じ）」とする。

### F-09 ファイルサイズの確認

設定を変えながら、書き出したときのファイルサイズと画質をその場で確かめられるようにする。

| 項目 | 仕様 |
|---|---|
| 書き出しサイズの表示 | 「書き出し」セクションに `書き出しサイズ 182.4 KB（元ファイルの 6%）` のように表示する。ステータスバーにも出す |
| 計算のしかた | 実際に書き出すのと同じ処理（5.8）で、メモリ上にファイルの中身を作り、そのバイト数を表示する。書き出すときはその中身をそのまま書く（5.6）ので、**表示されるサイズは書き出したファイルのサイズとバイト単位で一致する**（予想値ではない） |
| 計算のきっかけ | 書き出し設定（形式・オプション・品質の決め方・サイズ・背景色）、レシピ（枠・回転・フィルター）、表示する画像が変わったとき |
| 計算の頻度 | スライダーなどの操作中は 200 ms ごとに間引いて計算し、操作が止まったら必ず最後の値で計算する。新しい計算を始めたら、古い計算の結果は捨てる |
| 計算中 | 「計算中…」と表示する。計算中でも書き出しの操作はできる（書き出すときに必要なら計算し直す） |
| 速くする工夫 | 形式のオプションや品質の決め方だけが変わったとき（品質のスライダーなど）は、切り抜き・リサイズ・フィルター済みの画像を使い回し、エンコードだけをやり直す |
| 単位 | 1 KB = 1024 バイト。1 KB 未満は「B」、1 MB 未満は「KB」（小数 1 桁）、それ以上は「MB」（小数 2 桁）。ツールチップにバイト数をそのまま出す |
| 書き出しプレビュー | 中央の表示を［書き出しプレビュー］にすると、メモリ上に作ったファイルを読み直した画像（圧縮による劣化を含む）を表示する。読み直しは書き出しプレビューを表示しているときだけ行う |
| 表示のしかた | 「並べて比較」（左: 圧縮前、右: 圧縮後。ズーム・パンは左右で連動）と「書き出し結果のみ」を切り替える。既定は並べて比較・倍率 100%。右側の見出しに形式・使った品質・サイズを出す |
| プレビューの操作 | ズーム・パンは F-03 と同じ（道具は使わない）。書き出し設定を変えると、その場で右側（圧縮後）が更新される |
| ファイルサイズの上限 | 「ファイルサイズの上限を指定」のときは、上限以下になるいちばん高い品質を二分探索で探して使う（5.3）。見つけた品質を品質の入力欄とプレビューの見出しに表示する（例: 「品質 72 で 198.6 KB」） |
| 上限に収まらないとき | 品質をいちばん低くしても上限を超える場合は、いちばん低い品質で書き出し、M26 を出す |
| 一括書き出しでの上限 | 画像ごとに品質を探す（画像によって品質が変わる） |
| 一括書き出しの合計 | 一括書き出しの完了時に、書き出したファイルの合計サイズを M11 に出す |

### F-10 書き出し（1 枚）

| 項目 | 仕様 |
|---|---|
| 操作 | ［書き出し］ / `Ctrl+E`（表示中の画像）。［書き出して次へ］ / `Ctrl+Enter`（成功したら一覧の次の画像を表示。最後の画像なら表示はそのまま） |
| 内容 | 編集画像から枠の範囲を切り抜き、書き出しサイズにリサイズ（Lanczos3）し、フィルターをかけ、透過を保てない形式なら背景色で塗ってから、選んだ形式でエンコードする（5.8） |
| 書き込み方式 | 同じフォルダの一時ファイルに書いてからリネームする。既存のファイルは上書きしない。書き出し先が元画像と同じパスなら書かない（CoverCropper と同じ安全装置） |
| 出力先が未設定 / 存在しない | CoverCropper の F-04 と同じ（M05 / M06） |
| 書き出し後 | ステータスバーに M04。一覧の状態を「書き出し済み」にして回数を増やす。編集内容と履歴は保つ |

> `Ctrl+S` は、将来「編集内容の保存」に使うために空けておく（8 章）。

### F-11 一括書き出し

| 項目 | 仕様 |
|---|---|
| 操作 | ［すべて書き出し］ / `Ctrl+Shift+E` |
| 対象 | 一覧のすべての画像（状態にかかわらず） |
| 確認 | 始める前に M10 で確認する。出力先は始めるときに一度だけ確かめる（F-10 と同じ） |
| 内容 | 1 件ずつ元画像を読み込み、その画像のレシピで F-10 と同じ処理をして書き出す。一度も表示していない画像は初期状態のレシピで書き出す |
| 実行 | バックグラウンドで 1 件ずつ順に行う（メモリを抑えるため並列にしない） |
| 進捗 | ステータスバーに、プログレスバー・「一括書き出し中… 3 / 20: photo.jpg」・［中止］。一覧の状態も 1 件ごとに更新する |
| 中止 | ［中止］ / `Esc`。書き出し中の 1 件は最後まで終えてから止め、M12 |
| 失敗 | その画像を「エラー」にしてログに書き、次へ進む。失敗したときに出力先フォルダが無くなっていたら、残りも失敗するため止めて M06 |
| 完了 | M11 をダイアログで出す（失敗があれば警告アイコン） |
| 実行中にできること | ［中止］と、表示中の画像のズーム・パンだけ |

### F-12 設定

| 項目 | 仕様 |
|---|---|
| 保存場所 | `%APPDATA%\webDeza\settings.json`。CoverCropper とは別 |
| 項目 | 出力先フォルダ、前回「開く」で使ったフォルダ、書き出し設定（形式・形式ごとのオプション・品質の決め方と上限・サイズ・背景色）、切り抜きの比率、利用者のプリセット、ウィンドウの位置・サイズ・最大化、中央の表示（編集 / 書き出しプレビュー）と比較のしかた |
| 形式 | `System.Text.Json`。パラメーターの値は「キー → 数値・真偽・文字列」の JSON オブジェクトで書く |
| 読み込みの失敗 | JSON が壊れているときは既定値で起動し、M20（CoverCropper と同じ） |
| 値の検証 | 登録されていない形式 ID（プラグインを外した場合など）は JPEG にして M25。オプション・サイズ・上限が範囲外なら、その項目を既定値にする |
| 初回の引き継ぎ | webDeza の設定ファイルが無い初回の起動に限り、CoverCropper の `%APPDATA%\CoverCropper\settings.json` から保存先フォルダと前回のフォルダを読み取って使い、M19。CoverCropper の設定ファイルは読むだけで変更しない |
| ログ | `%APPDATA%\webDeza\logs\app.log`（書式は CoverCropper と同じ） |

---

## 3. CoverCropper（簡易版）の仕様

### 3.1 位置付け

CoverCropper は、共通エンジンの機能を固定の設定・固定の順番で呼ぶ簡易版とする。画面・操作・メッセージ・設定とログの場所・出力結果は、CoverCropper 設計書のとおりで変えない。変えるのは、内部の処理を共通エンジンと共通ライブラリに任せることだけである（手順は 5.5）。

### 3.2 固定の設定

| 項目 | 固定値 | 実現のしかた |
|---|---|---|
| 入力形式 | .jpg .jpeg .png .bmp .gif .tif .tiff .webp | `AppConstants.SupportedExtensions`。形式レジストリのうち、この拡張子の形式だけを使う |
| 透過 | 白で塗る | エンジンの `Flatten`（読み込み直後） |
| リサイズの大きさ | 横 1024 基準。縦が 768 未満なら縦 768 基準（CoverCropper 設計書 F-02） | エンジンの `CoverFit.CalculateSize` に 1024・768 を渡す |
| 補間 | Lanczos3 | エンジンの `Resize` |
| 切り抜き | 1024×768 | エンジンの `Crop` |
| 書き出し | JPEG・品質 85・メタデータなし | WIC プラグインの JPEG エンコーダーと `SafeFileWriter` |
| ファイル名 | `元名_yyyyMMdd_HHmmss.jpg` | `WebDeza.Core` の `OutputFileNamer`（拡張子 .jpg） |
| 処理の順序 | 読み込み → 白で塗る → 全体をリサイズ → 切り抜き → JPEG | CoverCropper の `MainViewModel`（今のまま） |

> CoverCropper で扱える形式を増やしたくなったら、`AppConstants.SupportedExtensions` の代わりに形式レジストリの一覧を使うだけでよい（今回はしない）。

---

## 4. 画面設計（webDeza）

### 4.1 メインウィンドウ

```
┌──────────────────────────────────────────────────────────────────────────────────────────────┐
│ [開く] [書き出し] [書き出して次へ] [すべて書き出し]  [元に戻す] [やり直し]                   │
├──────────────────┬─────┬───────────────────────────────────────┬─────────────────────────────┤
│ 画像一覧 (3)     │ [C] │ [編集] [書き出しプレビュー]           │ - 書き出し                  │
│ ┌──────────────┐ │ [H] │ ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │ プリセット [表紙画像     v] │
│ │  サムネイル  │ │     │ ░░░░■───────────────────────■░░░░░░░░ │ 形式       [JPEG         v] │
│ └──────────────┘ │     │ ░░░░│                       │░░░░░░░░ │ 品質       ------o--   [85] │
│ ✓ photo1.jpg     │     │ ░░░░│      切り抜き枠       │░░░░░░░░ │ サイズ     [幅x高さ      v] │
│ ┌──────────────┐ │     │ ░░░░│                       │░░░░░░░░ │            [1024] x [768]   │
│ │  サムネイル  │ │     │ ░░░░■───────────────────────■░░░░░░░░ │ 背景色     [白] #FFFFFF     │
│ └──────────────┘ │     │ ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │ 書き出しサイズ 182.4 KB (6%)│
│ * photo2.png     │     │                                       │ 出力先 D:\web\cover         │
│ ┌──────────────┐ │     │                                       │            [変更…] [開く]   │
│ │  サムネイル  │ │     │                                       │ - 切り抜き                  │
│ └──────────────┘ │     │                                       │ 比率 [書き出しサイズ     v] │
│ ! photo3.heic    │     │                                       │ [中央に戻す] [最大にする]   │
│                  │     │                                       │ - 回転・反転                │
│                  │     │                                       │ [左90] [右90] [左右] [上下] │
│                  │     │                                       │ - 基本補正       [リセット] │
│                  │     │                                       │ 明るさ       --o--       0  │
│                  │     │                                       │ コントラスト --o--       0  │
│ [一覧から外す]   │     │                                       │ 彩度         --o--       0  │
│ [一覧をクリア]   │     │       [全体] [100%] [-] [+]  37%      │ [フィルターを全画像に適用]  │
├──────────────────┴─────┴───────────────────────────────────────┴─────────────────────────────┤
│ 画像: 6000x2000 / 枠: (1666, 0) 2667x2000 / 書き出し: 1024x768 JPEG 182.4 KB (0.38 倍)       │
└──────────────────────────────────────────────────────────────────────────────────────────────┘
  一覧の印  ✓ 書き出し済み   * 書き出していない編集あり   ! エラー     [C] 切り抜き  [H] 手のひら
```

書き出しプレビューのとき（中央の領域だけ）:

```
┌─────────────────────────────────────────────────────────────┐
│ [編集] [書き出しプレビュー]    (o) 並べて比較  ( ) 結果のみ │
├──────────────────────────────┬──────────────────────────────┤
│ 圧縮前                       │ 圧縮後  JPEG 品質 72         │
│                              │         198.6 KB             │
│                              │                              │
│   左右は同じ位置・同じ       │   圧縮による劣化が           │
│   倍率で連動して動く         │   そのまま見える             │
│                              │                              │
├──────────────────────────────┴──────────────────────────────┤
│             [全体] [100%] [-] [+]  100%                     │
└─────────────────────────────────────────────────────────────┘
```

- 初期サイズ: 1440×900。最小: 1024×640。高 DPI は Per-Monitor V2（CoverCropper と同じ `app.manifest`）。
- タイトル: `{ファイル名} - webDeza`（画像が無いときは `webDeza`）。
- 画像が無いときは、キャンバスの中央に「ここに画像やフォルダをドラッグ＆ドロップ、または［開く］」。
- 編集パネルはスクロールでき、各セクションは折りたためる（`Expander`）。フィルターのセクションは登録されたフィルターの数だけ並ぶ。
- 書き出しプレビューの「圧縮前」は書き出す画像（切り抜き・リサイズ・フィルター済み）、「圧縮後」はそれをエンコードして読み直したもの。どちらも書き出し画像のピクセル座標で表示する。
- ボタンは CoverCropper と同じくフォーカスを取らない（`Focusable="False"`）。

### 4.2 各領域

| 領域 | 内容 |
|---|---|
| ツールバー（上） | 開く / 書き出し / 書き出して次へ / すべて書き出し ｜ 元に戻す / やり直し |
| 画像一覧（左・幅 220 DIP） | 件数、サムネイルの一覧、［一覧から外す］、［一覧をクリア］ |
| 道具（キャンバスの左） | 登録された道具のボタン（標準は 切り抜き・手のひら）。書き出しプレビューのときは無効 |
| 中央 | ［編集］ / ［書き出しプレビュー］の切り替え。編集はキャンバス（画像・切り抜きの表示・ハンドル）、書き出しプレビューは圧縮前後の比較。右下に倍率と［全体］［100%］［－］［＋］ |
| 編集パネル（右・幅 300 DIP） | 「書き出し」（書き出しサイズの表示を含む） ／ 「切り抜き」 ／ 「回転・反転」 ／ フィルターのセクション（登録順） ／ ［フィルターを全画像に適用］ |
| ステータスバー（下） | `画像: {編集画像の幅}×{高さ} ／ 枠: ({X}, {Y}) {幅}×{高さ} ／ 書き出し: {幅}×{高さ} {形式} {書き出しサイズ}（{書き出し倍率} 倍）／ {メッセージ}`。一括書き出し中はプログレスバーと［中止］ |

操作できない状態:

| 状態 | 無効にするもの |
|---|---|
| 画像が無い | 書き出し系、元に戻す / やり直し、一覧から外す、一覧をクリア、書き出しプレビュー、「切り抜き」「回転・反転」、フィルター、ズームのボタン（「書き出し」セクションは使える。書き出しサイズは「—」） |
| 読み込み中・書き出し中（1 枚） | 開く、書き出し系、一覧の操作、編集パネル、D&D。カーソルは待機 |
| 一括書き出し中 | ［中止］とズーム・パン以外のすべて |

### 4.3 キーボードショートカット

| キー | 動作 |
|---|---|
| `Ctrl+O` | 開く（複数選択可） |
| `Ctrl+E` | 書き出し（表示中の画像） |
| `Ctrl+Enter` | 書き出して次へ |
| `Ctrl+Shift+E` | すべて書き出し |
| `Ctrl+Z` | 元に戻す |
| `Ctrl+Y` / `Ctrl+Shift+Z` | やり直し |
| `P` | 中央の表示を 編集 ⇔ 書き出しプレビュー で切り替え |
| `C` / `H` | 道具: 切り抜き / 手のひら（道具の登録内容に従う。編集のとき） |
| `←↑→↓` / `Shift`+矢印 | 枠を書き出し画像の 1 px / 10 px 分移動（切り抜きの道具のとき） |
| `+` / `-` | 枠を 5% 大きく / 小さく（切り抜きの道具のとき） |
| `Home` / `Ctrl+Home` | 枠を中央に戻す / 最大にする |
| `Ctrl++` / `Ctrl+-` | 表示を拡大 / 縮小 |
| `Ctrl+0` / `Ctrl+1` | 全体表示 / 等倍表示（100%） |
| `Ctrl+R` / `Ctrl+Shift+R` | 右 / 左に 90° 回転 |
| `PageDown` / `PageUp` | 一覧の次 / 前の画像 |
| `Delete` | 選んでいる画像を一覧から外す（ファイルは削除しない） |
| `Esc` | 一括書き出しを中止 |
| `Space`（押している間） | 手のひら |

> テキスト入力欄・スライダー・コンボボックスにフォーカスがあるときは、修飾キーの無いキーはそのコントロールの操作を優先する。キャンバスをクリックすると、フォーカスはキャンバスに戻る。
> `+` / `-` はキーコード（`OemPlus` / `Add`、`OemMinus` / `Subtract`）で判定し、`Shift` の有無は問わない（日本語キーボードでは `+` を `Shift`+`;` で入力するため）。`Ctrl++` / `Ctrl+-` も同じ。

### 4.4 マウス操作

| 操作 | 動作 |
|---|---|
| 枠の内側をドラッグ（切り抜き） | 枠を移動 |
| ハンドルをドラッグ（切り抜き） | 枠の大きさを変更 |
| 枠の外側をドラッグ（切り抜き） / ドラッグ（手のひら・書き出しプレビュー） | パン（画像が領域に収まっているときは何もしない） |
| 中ボタンでドラッグ / `Space`+ドラッグ | パン（道具にかかわらず） |
| ホイール | ズーム（カーソル位置が中心） |
| スライダーをダブルクリック | その項目を既定値に戻す |

### 4.5 メッセージ一覧

| ID | 種別 | 文言 |
|---|---|---|
| M01 | エラー | 対応していないファイル形式です。 |
| M02 | エラー | 画像を読み込めませんでした。（{詳細}） |
| M03 | 警告(ステータス) | 切り抜いた範囲より大きく書き出すため拡大します（画質が低下する可能性があります）。 |
| M04 | 情報(ステータス) | 書き出しました: {ファイル名}（{サイズ}） |
| M05 | 確認 | 出力先フォルダが未設定です。フォルダを選択してください。 |
| M06 | エラー | 出力先フォルダが見つかりません: {パス} |
| M07 | エラー | 書き出しに失敗しました。（{詳細}） |
| M08 | （欠番） | CoverCropper の「最初の1件のみ読み込みました。」は、複数を読み込めるため使わない |
| M09 | 情報(ステータス) | {n} 件を一覧に追加しました。（除いたものがあれば続けて「重複 {d} 件・非対応 {u} 件を除きました。」） |
| M10 | 確認 | 一覧の {n} 件を書き出します。よろしいですか？（書き出し済みを含むときは続けて「書き出し済みの {k} 件も新しいファイルとして書き出します。」） |
| M11 | 情報 / 警告 | 一括書き出しが終わりました。成功 {s} 件 ／ 失敗 {f} 件 ／ 合計 {合計サイズ} |
| M12 | 情報(ステータス) | 一括書き出しを中止しました（{s} 件書き出し済み）。 |
| M13 | 確認 | 表示中の画像のフィルターの値を、一覧のすべての画像に適用しますか？ |
| M14 | 情報(ステータス) | フォルダに対応する画像がありませんでした。 |
| M15 | 警告(ステータス) | 一覧に入れられるのは {max} 件までです。{n} 件は追加しませんでした。 |
| M16 | 確認 | 書き出していない編集があります。一覧をクリアしますか？ |
| M17 | 確認 | 書き出していない編集があります。終了しますか？ |
| M18 | 確認 | 一括書き出し中です。中止して終了しますか？（「はい」なら書き出し中の 1 件が終わるのを待って終了する） |
| M19 | 情報(ステータス) | CoverCropper の保存先フォルダを引き継ぎました。 |
| M20 | 警告(ステータス) | 設定ファイルを読み込めなかったため、既定値で起動しました。 |
| M21 | エラー（M01 に続けて） | この形式を読むには {必要なもの} が必要です。（例: Microsoft Store の「HEIF 画像拡張機能」） |
| M22 | 情報(ステータス) | プリセット「{名前}」を保存しました。 |
| M23 | 確認 | プリセット「{名前}」を削除しますか？ |
| M24 | 情報(パネル) | {形式} は透過を保てないため、透過部分を背景色で塗ります。 |
| M25 | 警告(ステータス) | 形式「{ID}」が見つからないため、JPEG にしました。 |
| M26 | 警告(ステータス・パネル) | 品質をいちばん低くしても上限（{上限}）を超えます（{サイズ}）。 |
| M27 | 情報(書き出しプレビュー) | この形式は読み直せないため、圧縮後の画像を表示できません（サイズは表示します）。 |

---

## 5. 技術設計

### 5.1 技術スタック

| 項目 | 採用 |
|---|---|
| ランタイム | .NET 10。エンジンと Core は `net10.0`、WIC プラグインとアプリは `net10.0-windows`。x64 |
| UI | WPF |
| パターン | MVVM（`CommunityToolkit.Mvvm` 8.4.2。CoverCropper と同じ） |
| 画像処理 | エンジンは自前のピクセル処理（WPF に依存しない）。読み書きは WIC（WPF の `System.Windows.Media.Imaging`）を標準プラグインで使う |
| 設定 | `System.Text.Json` |
| 自然順の並べ替え | `StrCmpLogicalW`（shlwapi.dll）を P/Invoke |
| 時刻 | `TimeProvider`（履歴のまとめ判定などをテストで差し替えるため） |
| テスト | xUnit |
| 配布 | `dotnet publish WebDeza/WebDeza.csproj -c Release -r win-x64 --self-contained false`（CoverCropper も同様） |

### 5.2 プロジェクト構成

```
CoverCropper.slnx                       … 【変更】プロジェクトを追加（リポジトリ名とソリューション名は今は変えない）
WebDeza.Imaging/                        … 【新規】画像処理エンジン（net10.0。WPF を参照しない）
├─ ImageBuffer.cs                       … Bgra32 の画像データ
├─ PixelRect.cs / AspectRatio.cs / RgbColor.cs
├─ Geometry/
│  ├─ ImageOrientation.cs               … 回転・反転の組み合わせ
│  └─ Orientation.cs                    … 向きの適用（EXIF 番号でも可）
├─ Operations/
│  ├─ Crop.cs
│  ├─ Resize.cs                         … Lanczos3（CoverCropper の ImageResizer と同じ計算。透過は乗算済みで計算）
│  ├─ Flatten.cs                        … 背景色で塗って不透明にする
│  └─ CoverFit.cs                       … CoverCropper 設計書 F-02 のサイズ計算
├─ Parameters/
│  ├─ ParameterDescriptor.cs            … パラメーターの記述（UI の自動生成に使う）
│  └─ ParameterValues.cs                … パラメーターの値（不変・値で比較・JSON 化できる）
├─ Filters/
│  ├─ IImageFilter.cs / FilterRegistry.cs
│  └─ BasicAdjustFilter.cs              … 基本補正
├─ Formats/
│  ├─ IImageFormat.cs / IImageDecoder.cs / IImageEncoder.cs / SizeTuning.cs
│  ├─ FormatRegistry.cs                 … 形式の登録・検索・元ファイルの読み込み
│  └─ DecodedImage.cs / ImageMetadata.cs
├─ Editing/
│  ├─ EditRecipe.cs                     … 画像ごとの編集内容（不変）
│  ├─ CropAspect.cs / FrameRules.cs / CropFrameCalculator.cs
│  ├─ OutputSize.cs / ExportSettings.cs / ExportPreset.cs
│  ├─ RenderPipeline.cs                 … 表示用・書き出し用の画像を作る
│  ├─ ExportEncoder.cs / SizeTargetSearch.cs … メモリ上へのエンコードと、上限に合う品質の探索
│  └─ EditHistory.cs                    … 元に戻す / やり直し
└─ IO/
   └─ SafeFileWriter.cs                 … 一時ファイル → リネーム・上書きしない・元画像を守る
WebDeza.Imaging.Wic/                    … 【新規】標準の形式プラグイン（net10.0-windows・UseWPF）
├─ WicFormats.cs                        … 形式の登録（registry.AddWicFormats()）
├─ WicDecoder.cs                        … BitmapDecoder → ImageBuffer、向きの読み取り、サムネイル
├─ WicEncoders.cs                       … JPEG / PNG / BMP / TIFF
└─ BitmapSourceConverter.cs             … ImageBuffer ↔ BitmapSource（表示と CoverCropper 用）
WebDeza.Core/                           … 【新規】アプリ共通（net10.0）
├─ AppPaths.cs                          … %APPDATA%\{アプリ名} の組み立て
├─ AppLog.cs                            … ログ（書き込み先を指定できる）
├─ JsonSettingsService.cs               … 型を指定できる設定の読み書き
├─ OutputFileNamer.cs                   … 元名_日時.{拡張子}、重複時は _2, _3 …
└─ FileSizeFormatter.cs                 … バイト数の表示（B / KB / MB）
CoverCropper/                           … 簡易版。画面・ViewModel は変えない
└─ Services/                            … 【変更】中身を WebDeza.Imaging / WebDeza.Core の呼び出しに置き換える（public API は同じ）
WebDeza/                                … 【新規】webDeza（編集ソフト）。AssemblyName は webDeza
├─ AppInfo.cs                           … 製品名 "webDeza"。タイトル・ダイアログ・データフォルダに使う
├─ Composition/AppModules.cs            … 形式・フィルター・道具・プリセットの登録をまとめる所
├─ doc/webDeza_設計書.md                … 本書
├─ Models/                              … ImageItemStatus / AppSettings など
├─ Services/                            … ExportService / ExportPreviewService / BatchExporter / FileCollector /
│                                         NaturalStringComparer / SettingsService / PresetService / DialogService
├─ ViewModels/                          … MainViewModel / ImageItemViewModel / ExportSettingsViewModel /
│                                         ExportPreviewViewModel / FilterSectionViewModel / ParameterViewModel（種類ごと）
└─ Views/
   ├─ MainWindow.xaml(.cs)
   ├─ Canvas/                           … EditorCanvas / ViewTransform / ICanvasTool / CropTool / HandTool / ToolRegistry
   ├─ Preview/ExportPreviewView.xaml(.cs) … 圧縮前後の比較
   └─ Parameters/ParameterTemplates.xaml … パラメーターの種類ごとの入力欄
WebDeza.Imaging.Tests/                  … 【新規】エンジンと WIC プラグインのテスト
WebDeza.Tests/                          … 【新規】webDeza のテスト
CoverCropper.Tests/                     … CoverCropper のテスト（書き換えない）
```

依存の決まり:

| プロジェクト | 参照してよいもの |
|---|---|
| WebDeza.Imaging | .NET の標準ライブラリだけ（WPF・WIC・Core を参照しない） |
| WebDeza.Imaging.Wic | Imaging、WPF |
| WebDeza.Core | .NET の標準ライブラリだけ |
| CoverCropper / WebDeza | Imaging、Imaging.Wic、Core。WIC を直接呼ぶのは表示用の変換（`BitmapSourceConverter`）だけにし、読み書きは必ず形式レジストリを通す |

### 5.3 画像処理エンジン（WebDeza.Imaging）

**画像データ**

```csharp
/// <summary>8 bit Bgra32（ストレートアルファ）の画像。stride は Width * 4。作った後は中身を変えない約束とする</summary>
public sealed class ImageBuffer
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    public bool IsOpaque { get; }          // すべてのアルファが 255 なら true（作るときに決める）
    public static ImageBuffer Create(int width, int height, byte[] pixels, bool? isOpaque = null);
}
```

- 処理はすべて入力を変えずに新しい `ImageBuffer` を返す（入力は表示や履歴と共有されうるため）。
- 幅 × 高さ × 4 が `Array.MaxLength` を超える場合は `ImageTooLargeException`（アプリは M02 にする）。

**基本の処理**（すべて純粋関数。`CancellationToken` を受け取る）

| クラス | 内容 |
|---|---|
| `Orientation.Apply(image, ImageOrientation)` / `ApplyExif(image, int)` | 回転・反転。CoverCropper の `ImageLoader.ApplyOrientation` と同じ対応 |
| `Crop.Apply(image, PixelRect)` | 矩形を切り出す。はみ出す矩形は `ArgumentOutOfRangeException` |
| `Resize.Apply(image, width, height)` | Lanczos3 の分離型畳み込み（CoverCropper の `ImageResizer.ResizePixels` と同じ計算）。不透明な画像は CoverCropper と同じ計算、透過のある画像は乗算済みアルファで計算して、透明部分の色がにじまないようにする |
| `Flatten.Apply(image, RgbColor)` | 背景色で塗って不透明にする（CoverCropper の白背景合成と同じ式） |
| `CoverFit.CalculateSize(w, h, targetW, targetH)` | CoverCropper 設計書 F-02 の式を、目標サイズを引数にして一般化したもの |

**パラメーターの記述と値**（フィルターと出力形式のオプションで共用）

```csharp
public enum ParameterKind { Integer, Number, Boolean, Choice, Color }

public sealed record ParameterDescriptor(
    string Key, string DisplayName, ParameterKind Kind, object Default,
    double Min = 0, double Max = 0, double Step = 1, string? Unit = null,
    IReadOnlyList<ChoiceItem>? Choices = null);

public sealed record ChoiceItem(string Value, string DisplayName);

/// <summary>キー → 値（long / double / bool / string）。不変で、中身で比較できる。JSON にそのまま書ける</summary>
public sealed class ParameterValues : IEquatable<ParameterValues>
{
    public static ParameterValues Defaults(IEnumerable<ParameterDescriptor> descriptors);
    public T Get<T>(string key);
    public ParameterValues With(string key, object value);
    /// 記述に合わせて、無いキーを既定値で補い、範囲外を丸め、知らないキーを捨てる
    public ParameterValues Normalize(IEnumerable<ParameterDescriptor> descriptors);
}
```

**フィルター**

```csharp
public interface IImageFilter
{
    string Id { get; }                                    // "basic"。レシピ・プリセット・設定に書くので変えない
    string DisplayName { get; }                           // "基本補正"
    IReadOnlyList<ParameterDescriptor> Parameters { get; }
    bool IsIdentity(ParameterValues values);              // true なら Apply を呼ばない
    ImageBuffer Apply(ImageBuffer source, ParameterValues values, FilterContext context);
}

/// <summary>PixelScale: この画像の 1 px が書き出し画像の何 px に当たるか（書き出し時は 1。半径などを持つフィルター用）</summary>
public sealed record FilterContext(double PixelScale, CancellationToken CancellationToken);

public sealed class FilterRegistry
{
    public void Register(IImageFilter filter);            // 同じ Id は例外
    public IReadOnlyList<IImageFilter> All { get; }       // 登録順
    public IImageFilter? Find(string id);
}
```

**形式**

```csharp
[Flags]
public enum FormatCapabilities { None = 0, Decode = 1, Encode = 2, Alpha = 4 }

public interface IImageFormat
{
    string Id { get; }                                    // "jpeg"。設定・プリセットに書くので変えない
    string DisplayName { get; }                           // "JPEG"
    IReadOnlyList<string> Extensions { get; }             // 先頭が書き出すときの拡張子
    FormatCapabilities Capabilities { get; }
    string? Requirement { get; }                          // OS の拡張機能が要る場合の説明（M21）
    IImageDecoder? Decoder { get; }
    IImageEncoder? Encoder { get; }
}

public interface IImageDecoder
{
    /// 1 枚目を読み、向きを直した Bgra32 にする
    DecodedImage Decode(Stream stream, CancellationToken cancellationToken);
    /// 長辺 maxSize 以下のサムネイル（縮小デコード・埋め込みサムネイル可）。向きは直す
    ImageBuffer DecodeThumbnail(Stream stream, int maxSize, CancellationToken cancellationToken);
}

public sealed record ImageMetadata(int AppliedExifOrientation, int FrameCount);
public sealed record DecodedImage(ImageBuffer Image, string FormatId, ImageMetadata Metadata);

/// <summary>ファイルサイズを調整できる整数パラメーター（値が大きいほどファイルが大きくなる）の宣言</summary>
public sealed record SizeTuning(string ParameterKey);

public interface IImageEncoder
{
    IReadOnlyList<ParameterDescriptor> Options { get; }
    SizeTuning? SizeTuning { get; }                       // JPEG は "quality"。調整できない形式は null
    /// Alpha を持たない形式には、不透明にした画像が渡される（RenderPipeline が Flatten する）
    void Encode(ImageBuffer image, ParameterValues options, Stream output, CancellationToken cancellationToken);
}

public sealed class FormatRegistry
{
    public void Register(IImageFormat format);            // 同じ Id・同じ拡張子の重複は例外
    public IImageFormat? FindById(string id);
    public IImageFormat? FindByExtension(string extension);   // 大文字小文字は区別しない
    public IReadOnlyList<IImageFormat> Decodable { get; }
    public IReadOnlyList<IImageFormat> Encodable { get; }
    public bool CanDecode(string path);
    /// 元ファイルを FileMode.Open / FileAccess.Read / FileShare.Read で開き、読み終えたらすぐ閉じる。
    /// 元ファイルを開くのはこのクラスだけとする
    public DecodedImage Decode(string path, CancellationToken cancellationToken);
    public ImageBuffer DecodeThumbnail(string path, int maxSize, CancellationToken cancellationToken);
}
```

- 対応していない形式・デコードできない中身は `UnsupportedImageFormatException`（`NotSupportedException` の派生。CoverCropper の M01 の判定がそのまま使える）。

**編集モデル**

```csharp
public readonly record struct PixelRect(int X, int Y, int Width, int Height);
public readonly record struct AspectRatio(int Width, int Height);   // 約分して持つ
public readonly record struct RgbColor(byte R, byte G, byte B);

/// <summary>切り抜きの比率の選択（全画像共通）</summary>
public enum CropAspectKind { Free, Original, Fixed }
public sealed record CropAspect(CropAspectKind Kind, AspectRatio Ratio = default);

public enum OutputSizeMode { Exact, LongSide, Original }
public sealed record OutputSize(OutputSizeMode Mode, int Width, int Height, int LongSide);

/// <summary>書き出し設定（全画像共通）。FormatOptions は形式 ID → その形式のオプション。
/// SizeLimitBytes はファイルサイズの上限（形式のエンコーダーが SizeTuning を持つときだけ使う。null = 品質を指定）</summary>
public sealed record ExportSettings(
    string FormatId,
    IReadOnlyDictionary<string, ParameterValues> FormatOptions,
    long? SizeLimitBytes,
    OutputSize Size,
    RgbColor Background);

public sealed record ExportPreset(string Name, ExportSettings Settings, bool IsBuiltIn);

/// <summary>画像ごとの編集内容（不変）。Crop が null のときは初期状態。Filters に無いフィルターは既定値</summary>
public sealed record EditRecipe(
    ImageOrientation Orientation,
    PixelRect? Crop,
    IReadOnlyDictionary<string, ParameterValues> Filters)
{
    public static EditRecipe Initial { get; }
    // Equals / GetHashCode は Filters の中身で比べるように書く（record の既定は辞書を参照で比べてしまうため）
}
```

`ExportSettings` も辞書を持つので、`EditRecipe` と同じく中身で比べるように書く（書き出しプレビューの使い回しの判定に使う）。

**切り抜きの計算**（`CropFrameCalculator`。すべて純粋関数）

```
枠の規則（FrameRules）を、編集画像のサイズ（W, H）・比率の選択・書き出しサイズから作る
  比率 a
    書き出しサイズが Exact      → a = 書き出しの幅:高さ（比率の選択は無視）
    比率の選択が Free           → a なし
    比率の選択が Original       → a = W:H
    比率の選択が Fixed          → a = 指定の比率
  最大の枠
    a なし                      → 画像全体
    W × a.H ≥ H × a.W           → 幅 = min(W, round(H × a.W / a.H))
    それ以外                    → 幅 = W
    高さ = HeightForWidth(幅) = min(H, round(幅 × a.H / a.W))     … round は AwayFromZero、整数演算で行う
  最小の枠
    幅・高さとも 16 以上。Exact のときは幅 ≥ ceil(書き出し幅 ÷ 8)。最大を超える場合は最大に合わせる
  初期状態 = 最大の枠を中央に（端数は切り捨て）
```

| 関数 | 内容 |
|---|---|
| `Rules(W, H, CropAspect, OutputSize)` | 上の規則を作る |
| `Initial(rules)` / `Clamp(rect, rules)` / `Center(rect, rules)` | 初期状態 / 大きさを最小〜最大・位置を画像の内側に / 大きさを保って中央へ |
| `Move(rect, outputDx, outputDy, rules, size)` | 書き出し画像の px 単位で移動（F-04） |
| `ResizeAroundCenter(rect, newWidth, rules)` | 中心を保って大きさを変える |
| `ResizeFromHandle(rect, FrameHandle, pointerX, pointerY, rules)` | ハンドルのドラッグ。反対側を固定する |
| `Mirror(rect, horizontal, rules)` | 反転に合わせて位置を移す |
| `OutputPixelSize(rect, OutputSize)` | 書き出し画像のサイズ。Exact は (幅, 高さ)、LongSide は長辺が指定の長さになるよう縮尺 s = 長辺 ÷ max(枠の幅, 枠の高さ) を掛けて round（最低 1）、Original は枠のまま |

```csharp
public enum FrameHandle { TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left }
```

**処理の流れ**（`RenderPipeline`）

```csharp
public sealed class RenderPipeline
{
    public RenderPipeline(FilterRegistry filters);

    /// 書き出す画像: 向き → 切り抜き → リサイズ（大きさが同じなら省く） → フィルター（PixelScale = 1）
    ///              → encoderSupportsAlpha が false で不透明でなければ Flatten(背景色)
    public ImageBuffer RenderForExport(ImageBuffer source, EditRecipe recipe, CropAspect aspect,
        ExportSettings export, bool encoderSupportsAlpha, CancellationToken cancellationToken);

    /// 表示用の画像: 向き → 長辺 maxSide 以下に縮小 → フィルター。切り抜きはしない（キャンバスが枠を重ねて描く）
    public PreviewImage RenderPreview(ImageBuffer source, EditRecipe recipe, CropAspect aspect,
        ExportSettings export, int maxSide, CancellationToken cancellationToken);
}

/// <summary>Base はフィルター前、Image はフィルター後。EditedWidth/Height は向きを適用したフル解像度のサイズ</summary>
public sealed record PreviewImage(ImageBuffer Base, ImageBuffer Image, int EditedWidth, int EditedHeight);
```

**メモリ上へのエンコードと上限の探索**（F-09）

```csharp
/// <summary>エンコード済みのファイルの中身。UsedOptions は実際に使ったオプション（上限を指定したときは探した品質が入る）</summary>
public sealed record EncodedImage(byte[] Bytes, string FormatId, ParameterValues UsedOptions, bool FitsSizeLimit);

public sealed class ExportEncoder
{
    public ExportEncoder(FormatRegistry formats);
    /// 書き出す画像（RenderForExport の結果）を、書き出し設定の形式でメモリ上にエンコードする。
    /// SizeLimitBytes があり、エンコーダーが SizeTuning を持つときは SizeTargetSearch で品質を探す。
    /// ファイルへの書き出しはこの Bytes をそのまま書くので、表示したサイズと書き出したファイルのサイズは一致する
    public EncodedImage Encode(ImageBuffer exportImage, ExportSettings export, CancellationToken cancellationToken);
}

public static class SizeTargetSearch
{
    /// encode(v) の長さが limit 以下になる最大の v を、[min, max] の範囲から二分探索で探す。
    /// 長さが limit 以下だったものだけを候補にするので、Fits が true の結果は必ず limit 以下になる
    /// （値とサイズが単調でない場合も上限は守る）。エンコードの回数は ceil(log2(max − min + 1)) + 1 回以内（1〜100 なら 8 回以内）。
    /// 1 つも収まらなければ min の結果を返し、Fits = false
    public static (int Value, byte[] Bytes, bool Fits) FindLargest(
        int min, int max, long limit, Func<int, byte[]> encode, CancellationToken cancellationToken);
}
```

- 探索の範囲は、`SizeTuning.ParameterKey` が指すパラメーターの記述の Min〜Max（JPEG なら 1〜100）。

**履歴**（`EditHistory`）

```csharp
public sealed class EditHistory
{
    public EditHistory(EditRecipe initial, TimeProvider time, int capacity = 100);
    public EditRecipe Current { get; }
    public bool CanUndo { get; }
    public bool CanRedo { get; }
    /// 確定した変更を積む。直前と同じ coalesceKey で 1 秒以内なら、直前の 1 段を置き換える
    public void Commit(EditRecipe next, string? coalesceKey = null);
    public EditRecipe Undo();
    public EditRecipe Redo();
}
```

**安全な書き込み**（`SafeFileWriter`）

```csharp
public static class SafeFileWriter
{
    /// 同じフォルダの一時ファイル（.{名前}_{GUID}.tmp）に書いてからリネームする。
    /// path が既にあれば IOException。protectedPaths（元画像）とフルパスが同じなら InvalidOperationException。
    /// 失敗したら一時ファイルを消す（CoverCropper の JpegSaver の動作を一般化したもの）
    public static void Write(string path, ReadOnlySpan<byte> contents, IEnumerable<string> protectedPaths);
    public static void Write(string path, Action<Stream> write, IEnumerable<string> protectedPaths);
}
```

### 5.4 標準の形式プラグイン（WebDeza.Imaging.Wic）

| 形式 | ID | 拡張子 | 読み込み | 書き出し | 透過 | 書き出しのオプション |
|---|---|---|---|---|---|---|
| JPEG | `jpeg` | .jpg .jpeg .jpe .jfif | ○ | ○ | × | 品質（Integer 1〜100、既定 85）。`SizeTuning("quality")` |
| PNG | `png` | .png | ○ | ○ | ○ | インターレース（Boolean、既定 オフ） |
| BMP | `bmp` | .bmp .dib | ○ | ○ | × | なし |
| TIFF | `tiff` | .tif .tiff | ○（1 ページ目） | ○ | ○ | 圧縮（Choice: なし / LZW / ZIP、既定 LZW） |
| GIF | `gif` | .gif | ○（1 フレーム目） | × | ○ | 減色をしないため書き出しは対象外 |
| JPEG XR | `jxr` | .jxr .wdp | ○ | × | ○ | |
| ICO | `ico` | .ico | ○（最大の画像） | × | ○ | |
| WebP | `webp` | .webp | △ | × | ○ | 要: 「WebP 画像拡張機能」 |
| HEIF | `heif` | .heic .heif | △ | × | × | 要: 「HEIF 画像拡張機能」（HEVC も） |
| AVIF | `avif` | .avif | △ | × | ○ | 要: 「AV1 Video Extension」 |

△ = Windows に拡張機能があるときだけ読める。登録は常に行い、読めなかったら `UnsupportedImageFormatException` にして `Requirement` を M21 に出す。

- **デコード**: `BitmapDecoder.Create(stream, PreservePixelFormat, OnLoad)` → 対象のフレーム → 向きを読む（CoverCropper と同じクエリ `/app1/ifd/{ushort=274}`・`/ifd/{ushort=274}`）→ `FormatConvertedBitmap` で Bgra32 → 256 行ずつ `ImageBuffer` へ写す（CoverCropper と同じく巨大画像の一時メモリを抑える）→ エンジンの `Orientation.ApplyExif`。
- **エンコード**: 不透明な画像は Bgr24、透過のある画像は Bgra32 にして WPF のエンコーダーに渡す。メタデータ（サムネイル・EXIF・カラーコンテキスト）は付けない。同じ画像・同じオプションなら、毎回同じバイト列になること（W09）。
- **表示用の変換**: `BitmapSourceConverter.ToBitmapSource(ImageBuffer)`（96DPI・Bgra32・Freeze 済み）と `FromBitmapSource(BitmapSource)`。

### 5.5 CoverCropper を簡易版にする

ファイルは移さず、CoverCropper の中のクラスをそのまま残して、中身だけを共通部品の呼び出しに置き換える。これで CoverCropper のコード・名前空間・テストに手を入れずに済む。

1. 画面（Views）・ViewModel・App・DialogService・Messages・AppConstants・Models（AppSettings・CropState）は変えない。
2. 画像処理の 4 つのクラスは、public API（インターフェース・メソッド・戻り値が Freeze 済み 96DPI Bgr24 の `BitmapSource` であること・例外の種類）を変えずに、中身を `WebDeza.Imaging` / `WebDeza.Imaging.Wic` の呼び出しに置き換える。

   | CoverCropper のクラス | 置き換え後の中身 |
   |---|---|
   | `ImageLoader.Load` | 拡張子を `AppConstants.SupportedExtensions` で確かめる → `FormatRegistry.Decode` → `Flatten(白)` → Bgr24 の `BitmapSource` |
   | `ImageLoader.ApplyOrientation`（public static） | エンジンの `Orientation` を使う（Bgr24 の配列を受け取る形は残す） |
   | `ImageResizer.CalculateSize` / `Resize` | `CoverFit.CalculateSize(w, h, 1024, 768)` / `Resize.Apply` |
   | `ImageCropper.Clamp` / `Center` / `Crop` | Clamp と Center は今のまま。Crop は `Crop.Apply` |
   | `JpegSaver.Save` | WIC プラグインの JPEG エンコーダー（品質は引数）＋ `SafeFileWriter` |

3. 設定・ログ・ファイル名の 4 つのクラスも、public API を変えずに中身を `WebDeza.Core` の呼び出しに置き換える。

   | CoverCropper のクラス | 置き換え後の中身 |
   |---|---|
   | `AppPaths` | `WebDeza.Core.AppPaths.GetDataFolder("CoverCropper")` |
   | `AppLog` | `WebDeza.Core.AppLog` に CoverCropper のログファイルのパスを渡して書く |
   | `SettingsService` | `JsonSettingsService<AppSettings>`（`LastLoadError` などの動作は今と同じ） |
   | `OutputFileNamer` | `WebDeza.Core.OutputFileNamer`（拡張子 .jpg） |

4. 引数なしのコンストラクターは残す。その場合は、WIC の形式を登録した既定の形式レジストリ（CoverCropper の中に 1 つ持つ）を使う。`App.xaml.cs` も既存テストも引数なしで作っているので、どちらも変えずに済む。
5. `CoverCropper.csproj` に `WebDeza.Imaging`・`WebDeza.Imaging.Wic`・`WebDeza.Core` への参照を足す。既存テスト（53 件）は書き換えない。`CoverCropper.Tests` は `CoverCropper` を参照したままにする（共通部品は推移的に参照される）。
6. 出力が変わらないことを確かめる。置き換えの**前に**、代表的な入力（横長・縦長・4:3・小さい画像・大きな拡大・透過 PNG・回転情報あり・パレット画像・16 bit グレースケール）で CoverCropper の処理結果を基準ファイルとして保存しておき、置き換えた後に比べる（R05）。基準ファイルは `CoverCropper.Tests/Baseline/`（作り方と中身は同じフォルダの README）、比べるテストは `BaselineRegressionTests`。入力は可逆の形式だけにし、JPEG 出力は OS のコーデックに左右されない形で比べる（README を参照）。

### 5.6 webDeza の構成

**登録**（`Composition/AppModules.cs`）

```csharp
public static class AppModules
{
    public static void Register(FormatRegistry formats, FilterRegistry filters, ToolRegistry tools, PresetCatalog presets)
    {
        formats.AddWicFormats();                 // 標準の形式（5.4）
        filters.Register(new BasicAdjustFilter());
        tools.Register(new CropToolFactory());
        tools.Register(new HandToolFactory());
        presets.AddBuiltIns();                   // F-08 の組み込みプリセット
        // 形式・フィルター・道具を足すときは、ここに 1 行足す
    }
}
```

**ViewModel**

| クラス | 役割 |
|---|---|
| `MainViewModel` | 一覧・選択・読み込み・書き出し・一括書き出し・書き出し設定・比率の選択・中央の表示の切り替え。表示中の画像の `IEditorSession` を実装する |
| `ImageItemViewModel` | 一覧の 1 件。`Path`・`FileName`・`SourceFileSize`・`Thumbnail`・`Status`・`ErrorMessage`・`ExportCount`・`History`（`EditHistory`）・`LastExportedRecipe`。`HasUnexportedEdits` = `History.Current != EditRecipe.Initial && History.Current != LastExportedRecipe` |
| `ExportSettingsViewModel` | 形式の一覧（`FormatRegistry.Encodable`）、形式のオプションの入力欄、品質の決め方と上限、サイズ、背景色、出力先、プリセット、書き出しサイズの表示 |
| `ExportPreviewViewModel` | 書き出しプレビュー（圧縮前・圧縮後の画像、見出し、比較のしかた） |
| `FilterSectionViewModel` | フィルター 1 つ分のセクション。パラメーターの入力欄の一覧と［リセット］ |
| `ParameterViewModel` | パラメーター 1 つ分。`Kind` ごとの派生（Integer / Number / Boolean / Choice / Color） |

**入力欄の自動生成**（`Views/Parameters/ParameterTemplates.xaml`）

| 種類 | 入力欄 |
|---|---|
| Integer / Number | スライダー（Min〜Max、Step）と数値入力。ダブルクリックで既定値 |
| Boolean | チェックボックス |
| Choice | コンボボックス |
| Color | 色見本（白・黒・灰）と `#RRGGBB` の入力 |

- 専用の画面が要るフィルターは、フィルターの `Id` をキーにした DataTemplate を足すと、自動生成の代わりにそれが使われる。
- 品質の決め方が「ファイルサイズの上限」のときは、`SizeTuning` が指すパラメーターの入力欄を読み取り専用にし、探した値を表示する。

**キャンバスと道具**

```csharp
/// <summary>道具がレシピを読み書きするための窓口（MainViewModel が実装）</summary>
public interface IEditorSession
{
    EditRecipe Recipe { get; }
    int EditedWidth { get; }
    int EditedHeight { get; }
    FrameRules CropRules { get; }
    OutputSize OutputSize { get; }
    void Preview(EditRecipe recipe);                            // ドラッグ中（履歴に積まない）
    void Commit(EditRecipe recipe, string? coalesceKey = null); // 確定（履歴に積む）
    event EventHandler? Changed;
}

public interface ICanvasTool
{
    string Id { get; }                                          // "crop"
    string DisplayName { get; }                                 // "切り抜き"
    Key Shortcut { get; }                                       // C
    void OnPointerDown(CanvasPointer pointer);                  // 画面座標・画像座標・ボタン・修飾キー
    void OnPointerMove(CanvasPointer pointer);
    void OnPointerUp(CanvasPointer pointer);
    bool OnKeyDown(Key key, ModifierKeys modifiers);            // 処理したら true
    void Render(DrawingContext dc, ViewTransform view);         // ハンドルなどの重ね描き
    Cursor? GetCursor(CanvasPointer pointer);
}

/// <summary>道具は IEditorSession と ICanvasHost（再描画の依頼・パン・マウスのキャプチャ）を受け取って作る</summary>
public interface ICanvasToolFactory
{
    ICanvasTool Create(IEditorSession session, ICanvasHost host);
}
```

- `EditorCanvas`（`FrameworkElement`）は、市松模様・表示用画像・切り抜きの表示（枠外の暗さ・ガイド・枠線）を描き、マウスとキーを今の道具に渡す。中ボタン・`Space`・ホイールのズームは道具にかかわらずキャンバスが処理する。
- ズームの状態は表示だけの関心事なので、ViewModel には持たせずキャンバスが持つ。ViewModel は画像の切り替えと 90° 回転のときに `ResetViewRequest`（数値）を 1 増やし、キャンバスはその変化で全体表示に戻る。
- 書き出しプレビューの左右の表示は、`ViewTransform` を 1 つ共有して連動させる。

```csharp
/// <summary>表示倍率と位置。Scale は「画像の 1 px あたりの DIP」、Offset は画像の左上の表示位置</summary>
public readonly record struct ViewTransform(double Scale, double OffsetX, double OffsetY)
{
    public static ViewTransform Fit(Size viewport, int imageWidth, int imageHeight, double dpiScale, double margin);
    public ViewTransform ZoomAt(Point viewPoint, double factor, double minScale, double maxScale);
    public ViewTransform ClampPan(Size viewport, int imageWidth, int imageHeight);
    public Point ImageToView(double x, double y);
    public Point ViewToImage(Point viewPoint);
    public double ZoomPercent(double dpiScale) => Scale * dpiScale * 100;   // 100% = 1 物理ピクセル
}
```

**サービス**

| クラス | 役割 |
|---|---|
| `ExportPreviewService` | 書き出しサイズの表示と書き出しプレビュー用（F-09）。最後に作った「書き出す画像」（`RenderForExport` の結果）とエンコード結果を、条件と一緒に覚えておく。形式のオプションと上限だけが前回と違うときは、書き出す画像を作り直さずにエンコードだけをやり直す。新しい依頼が来たら古い計算の結果は捨てる。圧縮後の画像は、書き出しプレビューを表示しているときだけ読み直す |
| `ExportService` | 1 枚の書き出し。今の条件と同じ計算結果が `ExportPreviewService` にあればそのバイト列を、無ければその場で `RenderForExport` → `ExportEncoder` で作り、`SafeFileWriter` で書く（保護するパス = 元画像）。ファイル名は `OutputFileNamer`（拡張子は形式の先頭の拡張子） |
| `BatchExporter` | 一括書き出し: 1 件ずつ `FormatRegistry.Decode` → `RenderForExport` → `ExportEncoder` → `SafeFileWriter`。キャンセルは 1 件ごとの区切りで確かめる。進捗は `IProgress<BatchProgress>`、合計サイズを結果に入れる |
| `FileCollector` | D&D・開く・コマンドライン引数のパスを、追加する画像ファイルの並びにする（F-01 の規則） |
| `SettingsService` | 設定の読み書き・値の検証（F-12）・CoverCropper からの引き継ぎ |
| `PresetService` | 組み込みと利用者のプリセットの一覧・保存・削除 |

```csharp
public sealed record ExportPreviewResult(
    EncodedImage Encoded, int Width, int Height,
    ImageBuffer Rendered,          // 圧縮前（書き出す画像）
    ImageBuffer? Decoded);         // 圧縮後（書き出しプレビューを表示しているときだけ。読み直せない形式は null → M27）

public sealed record BatchJob(string SourcePath, EditRecipe Recipe);
public sealed record BatchItemResult(string SourcePath, string? SavedPath, long Bytes, string? Error);
public sealed record BatchProgress(int Done, int Total, BatchItemResult? Last);
public sealed record BatchResult(IReadOnlyList<BatchItemResult> Items, bool Cancelled, bool OutputFolderMissing, long TotalBytes);

public sealed record CollectResult(IReadOnlyList<string> Files, int Duplicates, int Unsupported,
                                   int OverLimit, bool FolderHadNoImages);
```

### 5.7 座標系と向き

| 名前 | 内容 | 使い道 |
|---|---|---|
| 元画像 | 形式レジストリで読んだ画像（向きを直した Bgra32） | 書き出しのたびの出発点。表示中の 1 枚だけメモリに持つ |
| 編集画像 | 元画像に `ImageOrientation` を適用したもの（フル解像度） | 切り抜きの座標系（`PixelRect`） |
| 表示用画像 | 編集画像を長辺 4096 px 以下に縮小したもの（フィルター前の `Base` とフィルター後の `Image`） | キャンバスの描画だけ |
| 書き出し画像 | `RenderForExport` の結果（書き出しサイズ） | エンコードと、書き出しプレビューの表示 |
| 画面 | DIP | マウス座標。`ViewTransform` で画像の座標と行き来する |

`ImageOrientation`（回転角 r と左右反転 f。適用の順序は「左右反転 → 回転」）と EXIF Orientation の対応:

| Rotation | FlipHorizontal | EXIF | 意味 |
|---|---|---|---|
| 0 | false | 1 | そのまま |
| 90 | false | 6 | 時計回り 90° |
| 180 | false | 3 | 180° |
| 270 | false | 8 | 反時計回り 90° |
| 0 | true | 2 | 左右反転 |
| 90 | true | 7 | 左右反転 → 時計回り 90°（反転置） |
| 180 | true | 4 | 上下反転 |
| 270 | true | 5 | 左右反転 → 反時計回り 90°（転置） |

操作:

| 操作 | 新しい (r, f) |
|---|---|
| 右に 90° | ((r + 90) mod 360, f) |
| 左に 90° | ((r + 270) mod 360, f) |
| 左右反転 | ((360 − r) mod 360, !f) |
| 上下反転 | ((540 − r) mod 360, !f) |

> 今の変換を T = R(r)∘F^f（F^f を先に、R(r) を後に適用）と書くと、画面上での左右反転は T の後に F を適用する操作で、F∘R(r)∘F^f = R(−r)∘F^(1−f) になる。上下反転は R(180)∘F なので R(180−r)∘F^(1−f) になる。

### 5.8 処理の順序

**書き出し（1 枚・一括とも同じ）**

```
元ファイル
  → FormatRegistry.Decode          … 元画像（向きを直した Bgra32。1 枚の書き出しではメモリのものを使う）
  → Orientation.Apply              … 編集画像
  → Crop.Apply(枠)                 … 枠が null なら CropFrameCalculator.Initial、あれば Clamp して使う
  → Resize.Apply                   … OutputPixelSize の大きさへ（同じなら省く）
  → フィルター（登録順）           … IsIdentity のものは省く
  → Flatten(背景色)                … 形式が透過を保てず、画像が不透明でないときだけ
                                     ここまでが「書き出し画像」（ExportPreviewService が使い回す）
  → ExportEncoder.Encode           … メモリ上にエンコード。上限があれば品質を探す
                                     ここまでが「書き出しサイズの表示」に使うバイト列
  → SafeFileWriter.Write           … そのバイト列を一時ファイル → リネーム。元画像のパスには書かない
```

**表示**

```
元画像 → Orientation.Apply → 長辺 4096 px 以下に縮小 → Base → フィルター → Image → EditorCanvas
```

- 90° 回転・反転のときは、元画像から作り直さず、`Base` に差分の変換（右回転なら (90, false)、左右反転なら (0, true) など）を適用してからフィルターをかけ直す。書き出しは常に元画像と合成した `ImageOrientation` から作るので、表示と書き出しがずれないことを E03 で確かめる。
- フィルターの値を変えたときは、`Base` にフィルターをかけ直すだけにする。

**書き出しサイズの表示・書き出しプレビュー**

```
設定・レシピ・画像の変更
  → 200 ms ごとに間引く
  → ExportPreviewService
       書き出し画像の条件（レシピ・比率・サイズ・背景色・透過を保てるか）が前回と同じ → 書き出し画像を使い回す
       違う → RenderForExport で作り直す
  → ExportEncoder.Encode → バイト数を表示
  → （書き出しプレビューを表示しているとき）同じ形式のデコーダーで読み直す → 圧縮後として表示
```

**CoverCropper（簡易版）**

```
元ファイル → FormatRegistry.Decode → Flatten(白) → CoverFit + Resize（全体） → Crop(1024×768) → JPEG(85) → SafeFileWriter
```

### 5.9 名称の表記

| 使う所 | 表記 |
|---|---|
| 製品名（タイトル・ダイアログ・README・exe 名・データフォルダ） | `webDeza`（`AppInfo.ProductName`。exe は `webDeza.exe`、データフォルダは `%APPDATA%\webDeza`） |
| プロジェクト名・名前空間・型名 | `WebDeza`（.NET の命名規則に合わせて先頭を大文字にする。例: `WebDeza.Imaging`） |
| 読み | ウェブデザ |

- `WebDeza.csproj` の `AssemblyName` を `webDeza`、`RootNamespace` を `WebDeza` にする。
- リポジトリ名とソリューション名（`CoverCropper`）は今は変えない。変える場合は CI と README も直す。

### 5.10 実装上の注意点

1. **元画像の保護**: 元ファイルを開くのは `FormatRegistry` だけにする。書き込みは `SafeFileWriter` だけにし、保護するパスに元画像を必ず渡す。CoverCropper の設定ファイルは引き継ぎで読むだけにする。
2. **非同期と古い結果の破棄**: 読み込み・表示用画像の作成・フィルターのかけ直し・書き出しサイズの計算・書き出しは `Task.Run` で行う。画像の切り替え・フィルターのかけ直し・書き出しサイズの計算は、連番かキャンセルで「最後に頼んだもの」だけを画面に反映する。
3. **メモリ**: 次の画像を読む前に、前の画像への参照を外す。8000×6000 では、元画像 約 192 MB、表示用画像 2 枚 約 128 MB、書き出し時の一時的な画像 最大約 400 MB を見込む。書き出しサイズが「指定しない」で画像が大きいときは、書き出しプレビューの圧縮前・圧縮後も同じだけ要る。`OutOfMemoryException` は捕まえて M02 / M07。一括書き出しは並列にしない。
4. **Resize の互換性**: CoverCropper の出力を変えないため、不透明な画像の計算（重みの作り方・足す順番・丸め）は CoverCropper の `ImageResizer.ResizePixels` と同じにする。
5. **書き出しサイズの一致**: 表示するサイズと書き出したファイルのサイズを一致させるため、ファイルに書くのは必ず `ExportEncoder` が作ったバイト列にする（エンコーダーを直接ファイルに書かせない）。
6. **プラグインの例外**: フィルター・エンコーダー・デコーダーが投げた例外はアプリを落とさず、読み込みなら M02、書き出しなら M07、書き出しサイズの計算なら表示を「—」にしてログに書く。
7. **キー操作とフォーカス**: ウィンドウの `PreviewKeyDown` で受け、4.3 の注記に従う。修飾キーの無いキーは今の道具の `OnKeyDown` に先に渡す。
8. **高 DPI**: 100% の倍率は `VisualTreeHelper.GetDpi(this).DpiScaleX` から求め、`OnDpiChanged` で求め直す。
9. **起動**: `App.OnStartup` の最初に、ログ・設定の場所を `AppPaths.GetDataFolder(AppInfo.ProductName)` で決めてから、`AppModules.Register` で登録する。未処理例外の扱いは CoverCropper と同じ（タイトルは webDeza）。
10. **CoverCropper と同時に動かす**: 二重起動の禁止はしない。設定・ログが別なので同時に動かしてよい。

### 5.11 処理シーケンス

**画像を選び、品質を調整して書き出す**

```
User      MainWindow/VM          FormatRegistry  RenderPipeline  ExportPreviewService  ExportService
 |--選択----->|                        |                |                 |                  |
 |            |--Decode(path)--------->|                |                 |                  |
 |            |--RenderPreview(元画像, レシピ, …)------>|                 |                  |
 |<-表示+枠---|  （ResetViewRequest++）                 |                 |                  |
 |            |--ComputeAsync(元画像, レシピ, 比率, 書き出し設定)-------->|                  |
 |            |                        |  RenderForExport → ExportEncoder.Encode           |
 |<-182.4 KB--|<-------------------------------------------------------- |                  |
 |--品質 85→70|--ComputeAsync（オプションだけ違う: エンコードだけやり直す）->|                  |
 |<-128.0 KB--|<-------------------------------------------------------- |                  |
 |--Ctrl+E--->|--Export(…)---------------------------------------------------------------->|
 |            |                        |                |  （同じ条件のバイト列を使う）→ SafeFileWriter
 |<-書き出し完了-|  （状態 = 書き出し済み、LastExportedRecipe = 今のレシピ）                     |
```

**一括書き出し**

```
User        MainWindow/VM                  BatchExporter
 |--すべて書き出し->|--M10 で確認・出力先の確認   |
 |                  |--RunAsync(jobs, …)-------->|
 |                  |                            |--1 件目: Decode → RenderForExport → Encode → Write
 |<--進捗-----------|<--BatchProgress(1/20)------|
 |--中止----------->|--Cancel()                  |
 |                  |                            |（書き出し中の 1 件を終えて止まる）
 |<--M12------------|<--BatchResult(Cancelled)---|
```

---

## 6. テスト設計

### 6.1 単体テスト: エンジンと WIC プラグイン（WebDeza.Imaging.Tests）

| No | 対象 | 入力 | 期待結果 |
|---|---|---|---|
| E01 | ImageOrientation の EXIF 対応 | 8 通りの (r, f) | 5.7 の表の番号 |
| E02 | ImageOrientation の操作 | 右回転 4 回 / 左回転 4 回 / 左右反転 2 回 / 上下反転 2 回 | そのまま（0, false） |
| E03 | ImageOrientation ＋ Orientation.Apply | 4 つの操作をランダムに並べた列 1000 通り（長さ 1〜10）を 3×2 の画像に適用 | 1 操作ずつ差分で適用した画像と、合成した向きで 1 回適用した画像が全画素一致 |
| E04 | Crop.Apply | 任意の矩形 / はみ出す矩形 | 矩形の大きさで指定位置の画素 / ArgumentOutOfRangeException |
| E05 | Resize.Apply | 縮小・拡大・等倍 / 単色 | 指定の大きさ / 色が変わらない / 等倍はそのまま |
| E06 | Resize.Apply | 左半分が透明な黒 (0,0,0,0)、右半分が不透明な白 | 縮小後、不透明な部分の色が灰色にならない（透明部分の色がにじまない） |
| E07 | Resize.Apply | 不透明な画像（`CoverCropper.Tests/Baseline/expected/*.loaded.png`） | 同じフォルダの `*.resized.png`（置き換え前の CoverCropper の ImageResizer の結果）と全画素一致 |
| E08 | Flatten.Apply | 半透明の赤を白で | CoverCropper の白背景合成と同じ値 |
| E09 | CoverFit.CalculateSize | CoverCropper 設計書 U01〜U07 の入力（目標 1024×768） | U01〜U07 の期待値 |
| E10 | BasicAdjustFilter | 既定値 / 明るさ ±100 / コントラスト ±100 / 彩度 −100 / 彩度 +100 の灰色 | IsIdentity / 0 と 255 は変わらず 128 は増減 / +100 で 32→0・224→255、−100 で 0→64・255→191 / R=G=B / 変わらない |
| E11 | BasicAdjustFilter | 半透明の画像 | アルファが変わらない |
| E12 | ParameterValues | 既定値 / With / 中身が同じ 2 つ / Normalize（範囲外・無いキー・知らないキー） | 記述の既定値 / 元は変わらない / Equals が true / 丸め・補い・捨てる |
| E13 | FormatRegistry | 拡張子の大文字小文字 / 同じ Id・同じ拡張子の登録 / Decodable・Encodable | 見つかる / 例外 / 能力どおりに分かれる |
| E14 | SafeFileWriter | 既にあるパス / 書き込み中の例外 / 保護するパス | IOException で中身は変わらない / 一時ファイルが残らない / InvalidOperationException |
| E15 | CropFrameCalculator 最大の枠 | 6000×2000・3000×4000・4000×3000（Exact 1024×768）、1920×1080（Exact 1080×1080） | 2667×2000・3000×2250・4000×3000・1080×1080 |
| E16 | CropFrameCalculator 初期状態 | 6000×2000（Exact 1024×768）/ 任意（Free） | (1666, 0, 2667, 2000) / 画像全体 |
| E17 | CropFrameCalculator | 画像・比率・書き出しサイズとも乱数で 1000 件 | 枠が画像の内側、最小〜最大、比率の誤差が 0.5 px 以下 |
| E18 | CropFrameCalculator 最小の枠 | 6000×4000（Exact 1024×768）/ 100×75（Exact 1024×768）/ Free | 幅 128 / 幅 100（最大に合わせる）/ 16×16 |
| E19 | CropFrameCalculator.Move | 枠の幅 2667・Exact 幅 1024 で 1 / 10 / 枠の幅 300 で 1 | 3 / 26 / 1（最低 1） |
| E20 | CropFrameCalculator.ResizeFromHandle | 比率固定で右下を外へ / 左上を越えて / Free で右の辺 | 左上固定で端で止まる / 最小で止まり裏返らない / 幅だけ変わる |
| E21 | CropFrameCalculator.ResizeAroundCenter / Mirror | 中ほど・端の近く / 左右・上下反転 | 中心を保つ・内側へずれる / X' = W − X − 幅・Y' = H − Y − 高さ |
| E22 | CropFrameCalculator.OutputPixelSize | Exact / LongSide 1920 で枠 3000×2000 / Original | 指定の大きさ / 1920×1280 / 枠のまま |
| E23 | EditHistory | Commit 3 回 → Undo 2 回 → Redo 1 回 / Undo 後の Commit / 101 回 Commit / 同じキーで 1 秒以内に 2 回（TimeProvider を差し替え） | 期待どおりのレシピ / Redo できない / 最古が消える / 1 段にまとまる |
| E24 | EditRecipe / ExportSettings | 辞書の中身が同じで別の辞書 | Equals が true |
| E25 | RenderPipeline.RenderForExport | 4:3 の画像・Exact 1024×768・初期状態のレシピ | 全体を 1024×768 に Resize した画像と全画素一致 |
| E26 | RenderPipeline.RenderForExport | 透過のある画像を、透過を保てる / 保てない形式で | アルファが残る / 背景色で塗られて不透明 |
| E27 | RenderPipeline.RenderPreview | 長辺 8000 の画像 | 長辺 4096 以下で、EditedWidth / Height は向きを適用したフル解像度 |
| E28 | SizeTargetSearch | サイズ = 値 × 10 の偽のエンコーダー（1〜100）で上限 555 / 上限 5 | 55 で Fits / 1 で Fits = false。どちらもエンコードは 8 回以内 |
| E29 | SizeTargetSearch | サイズが値に対して単調でない偽のエンコーダー、上限を乱数で 1000 通り | Fits が true の結果は必ず上限以下 |
| E30 | ExportEncoder | JPEG・上限あり / PNG・上限あり（SizeTuning なし） | バイト数が上限以下で UsedOptions に探した品質 / 上限を無視して PNG のオプションどおり |
| W01 | WIC デコード | JPEG・PNG・BMP・GIF・TIFF・ICO の小さなファイル | 正しい大きさの Bgra32。ICO は最大の画像 |
| W02 | WIC デコード | EXIF Orientation 6 の JPEG | 時計回りに回転済み、AppliedExifOrientation = 6 |
| W03 | WIC デコード | 透過 PNG | アルファが残る（白で塗られない） |
| W04 | WIC デコード | 読み込んだファイル | ハッシュと更新日時が変わらず、読み込み後に削除できる |
| W05 | WIC エンコード | JPEG の品質 50 と 95 | 95 のほうが大きい。メタデータが無い |
| W06 | WIC エンコード | PNG（透過あり）・TIFF（LZW / ZIP）・BMP を書いて読み直す | 全画素一致（BMP は不透明にしたもの） |
| W07 | WIC デコード | 拡張子が .jpg で中身が PNG | 読める |
| W08 | BitmapSourceConverter | ImageBuffer → BitmapSource → ImageBuffer | 全画素一致 |
| W09 | WIC エンコード | 同じ画像・同じオプションで 2 回 | 2 回のバイト列が一致する |

### 6.2 単体テスト: webDeza（WebDeza.Tests）

| No | 対象 | 入力 | 期待結果 |
|---|---|---|---|
| V01 | ExportService | 書き出し先が元画像と同じになる（ファイル名の生成を差し替え） | 書かない（例外） |
| V02 | ExportService | 形式 PNG / TIFF | 拡張子が .png / .tif |
| V03 | ExportService | JPEG の品質 50 | エンコーダーに 50 が渡る（エンコーダーを差し替え） |
| V04 | ExportService | 書き出しサイズを計算した直後に書き出す | ファイルの中身が計算したバイト列と一致（エンコードは 1 回だけ） |
| V05 | ExportPreviewService | 品質だけを 3 回変える / レシピを変える | RenderForExport は 1 回、エンコードは 3 回 / RenderForExport をやり直す |
| V06 | ExportPreviewService | 計算中に次の依頼が来る | 古い結果は通知されず、最後の依頼の結果だけが通知される |
| V07 | ExportPreviewService | 書き出しプレビューを表示していない / 表示している | 圧縮後を読み直さない（Decoded が null）/ 読み直す |
| V08 | BatchExporter | 3 件中 2 件目が読めない | 1・3 件目は書き出し、2 件目はエラー、最後まで進む |
| V09 | BatchExporter | 1 件目の書き出し中に中止 | 1 件目は書き出し、2 件目以降は処理せず Cancelled |
| V10 | BatchExporter | 2 件目の失敗時に出力先フォルダが無い | そこで止まり OutputFolderMissing |
| V11 | BatchExporter | 上限 300 KB で 3 件 | 3 件とも 300 KB 以下。TotalBytes が各ファイルの合計と一致 |
| V12 | FileCollector | サブフォルダのあるフォルダ | 直下の対応ファイルだけ |
| V13 | FileCollector | 一覧と重複 / 入力の中で重複（大文字小文字違い）/ 非対応 | 除外し、それぞれの件数を数える |
| V14 | FileCollector | `a10.jpg`, `a2.jpg`, `a1.jpg` | `a1`, `a2`, `a10` の順 |
| V15 | FileCollector | 空きが 2 件のところに 5 件 | 2 件を返し、超過 3 件 |
| V16 | SettingsService | 範囲外のサイズ・オプション・上限 / 登録されていない形式 ID | 既定値 / JPEG にして警告 |
| V17 | SettingsService | webDeza の設定なし・CoverCropper の設定あり / 両方あり | 保存先と前回のフォルダを引き継ぎ、CoverCropper のファイルの内容と更新日時は変わらない / 引き継がない |
| V18 | SettingsService | 書き出し設定・比率・利用者のプリセットを保存して読み直す | 同じ値 |
| V19 | PresetService | 組み込みの「表紙画像」 | JPEG・品質 85・Exact 1024×768（CoverCropper の AppConstants と同じ値） |
| V20 | PresetService | 利用者のプリセットを保存 → 削除 / 組み込みを削除 | 一覧に増えて消える / できない |
| V21 | FileSizeFormatter | 1023 / 1024 / 186,778 / 1,536,000 | `1023 B` / `1.0 KB` / `182.4 KB` / `1.46 MB` |
| V22 | ParameterViewModel | Integer の記述（0〜100）に 150 を入れる / 既定値に戻す | 100 になる / 既定値 |
| V23 | ExportSettingsViewModel | 形式を JPEG → PNG → JPEG / 品質の決め方を上限に | JPEG の品質が保たれている / 品質の入力欄が読み取り専用 |
| V24 | ViewTransform.Fit | 小さい画像 / 大きい画像で dpiScale 1.5 | 100% で中央 / 収まり Scale ≤ 1 ÷ 1.5 |
| V25 | ViewTransform.ZoomAt / ClampPan | 任意の点で拡大縮小 / 大きくパン | その点の下の画像座標が変わらず倍率は範囲内 / 画像の端が領域の内側に入らない |
| V26 | MainViewModel | 書き出しの縦横比を変える / 同じ縦横比に変える / 比率の選択を変える | 全画像の Crop が null / そのまま / 全画像の Crop が null |
| V27 | MainViewModel | フィルターを全画像に適用 | 全画像の Filters が同じ。Orientation と Crop は変わらない。各画像の履歴に 1 段積まれる |
| V28 | MainViewModel | 書き出して次へ | 状態が書き出し済み、次の画像が選ばれ、HasUnexportedEdits が false |
| V29 | MainViewModel | 画像 A を編集 → B に切り替え → A に戻って Undo | A の編集が 1 段戻る（B は変わらない） |
| V30 | CropTool | 比率固定で右下のハンドルをドラッグして離す | ドラッグ中は Preview、離したときに Commit が 1 回 |

ViewModel と道具のテストでは、形式・エンコーダー・ダイアログ・`IEditorSession` を差し替えて、ファイルや画面を使わずに確かめる。

### 6.3 回帰確認: CoverCropper

| No | 確認内容 |
|---|---|
| R01 | 既存の単体テスト（53 件）が、テストを書き換えずにすべて成功する |
| R02 | CoverCropper を起動し、読み込み・枠の移動（ドラッグ・矢印キー）・保存・保存先の変更・ウィンドウ位置の保存が今までどおり動く（CoverCropper 設計書 5.2 の T03・T04・T05・T06・T07・T08 を中心に） |
| R03 | 設定とログが `%APPDATA%\CoverCropper\` のまま |
| R04 | CoverCropper の配布用ビルドに必要な DLL（WebDeza.Imaging・WebDeza.Imaging.Wic・WebDeza.Core）が含まれ、そのフォルダだけで起動できる |
| R05 | `BaselineRegressionTests` が成功する。読み込み・リサイズ・切り抜きの結果が基準と全画素一致し、JPEG 出力が「品質 85・メタデータなし」の参照とバイト単位で一致すること（一致しない場合は差の最大と理由を記録し、各チャンネル 1 以下であることを確かめてから判断する） |

### 6.4 結合・手動テスト: webDeza

| No | 観点 | 確認内容 |
|---|---|---|
| T01 | 元画像の保護 | 読み込み・編集・書き出し・一括書き出しの後で、元画像の更新日時とハッシュが変わっていない |
| T02 | ファイルロック | 一覧にある画像・表示中の画像を、エクスプローラーで削除・リネームできる |
| T03 | 読み込み | 複数ファイル・フォルダ・非対応の混在・重複・500 件超の D&D、複数選択の「開く」、コマンドライン引数 |
| T04 | 形式 | 5.4 の各形式を読めること。拡張機能が無い環境の HEIF で M01 と M21 が出ること |
| T05 | EXIF | スマホの縦撮り写真が、キャンバス・サムネイル・書き出し結果のすべてで正しい向き |
| T06 | 透過 | 透過 PNG が市松模様の上に表示され、PNG で書き出すと透過が残り、JPEG で書き出すと背景色で塗られる |
| T07 | 切り抜き | 比率の各選択・縦横の入れ替え・ハンドル（固定 / 自由）・矢印・`+` / `-`・中央に戻す・最大にする。はみ出さず、比率が保たれる |
| T08 | ズーム・パン | ホイールでカーソル位置を中心に拡大縮小。3 通りのパン、全体表示 / 100%、ウィンドウサイズの変更 |
| T09 | 高 DPI | 150% / 200% のスケーリングで、100% 表示が物理ピクセルの 1:1。枠と書き出し結果がずれない |
| T10 | 回転・反転 | 書き出し結果の向きがキャンバスと同じ。サムネイルにも反映される |
| T11 | フィルター | キャンバスと書き出し結果の見た目が一致する。リセット・全画像に適用 |
| T12 | 元に戻す | 枠・回転・フィルターの各操作を戻せて、やり直せる。画像を切り替えても別々の履歴になっている |
| T13 | 書き出し設定 | 各形式とオプション・サイズの 3 方式・背景色・プリセットの保存と削除。再起動後も保たれる |
| T14 | 書き出しサイズ | JPEG の品質のスライダーを動かすと書き出しサイズの表示が追いかけて変わる。書き出したファイルのサイズ（エクスプローラーのプロパティのバイト数）が表示と一致する |
| T15 | 書き出しプレビュー | 品質を下げると圧縮後にブロックノイズが見える。左右のズーム・パンが連動する。`P` で切り替わる |
| T16 | ファイルサイズの上限 | 上限 200 KB で書き出したファイルが 200 KB 以下で、使った品質が表示される。とても小さい上限で M26 |
| T17 | 一括書き出し | 20 件で進捗・中止・失敗の混在・途中で出力先を消したとき。M11 の合計サイズ |
| T18 | 大きな画像 | 8000×6000 程度で、選択から表示・ズーム・フィルターのプレビュー・書き出しサイズの計算が実用的な速さ |
| T19 | 終了時の確認 | 書き出していない編集があるとき M17、一括書き出し中は M18 |
| T20 | CoverCropper との分離 | CoverCropper と webDeza を同時に動かし、互いの設定・ログが変わらない。初回起動で保存先を引き継ぐ |

---

## 7. 受け入れ基準

- [ ] CoverCropper の画面・動作・出力結果が変わっていない（6.3 の R01〜R05 をすべて満たす）
- [ ] CoverCropper の画像処理と設定・ログ・ファイル名の処理が共通部品を通っている（CoverCropper の Services に独自の処理が残っていない）
- [ ] 形式・フィルター・道具を、`AppModules` への登録だけで足せる（画面のコードを変えずに、テスト用の形式とフィルターを足して確かめる）
- [ ] 5.4 の形式を読み込め、JPEG / PNG / BMP / TIFF で書き出せる
- [ ] 複数の画像・フォルダを読み込み、一覧で切り替えられる
- [ ] 切り抜き（比率指定・自由）、ズーム・パン、回転・反転、基本補正ができ、書き出し結果に反映される
- [ ] 元に戻す / やり直しが画像ごとにできる
- [ ] 設定を変えるたびに書き出しサイズが表示され、書き出したファイルのサイズとバイト単位で一致する
- [ ] 書き出しプレビューで圧縮前後を並べて比べられる
- [ ] JPEG でファイルサイズの上限を指定でき、書き出したファイルが上限以下になる
- [ ] 書き出し設定とプリセットが保たれ、1 枚ずつの書き出しと一括書き出し（中止できる）ができる
- [ ] 元画像ファイルが一切変更されない
- [ ] 6 章の単体テストがすべて成功する

---

## 8. 対象外（将来の拡張と、足す場所）

| 機能 | 足す場所 |
|---|---|
| 減色（GIF・PNG8 など 256 色以下への変換）、GIF の書き出し、アニメーション GIF | 行わない（1.4。今の回線速度では必要性が低い）。もし必要になれば、形式プラグインのエンコーダーとして足せる |
| WebP・AVIF・HEIF の書き出し | 形式プラグイン（外部ライブラリを使う別プロジェクト）。`SizeTuning` を宣言すれば、書き出しサイズの表示と上限の指定がそのまま使える |
| 形式ごとのサイズの比較（JPEG・PNG・WebP を一度に計算して並べる） | `ExportPreviewService` を形式の数だけ回す |
| カメラ RAW などの読み込み | 形式プラグイン |
| メタデータを残す（撮影日時など。位置情報は除く） | `ImageMetadata` とエンコーダーのオプション |
| ICC プロファイル（色空間の変換） | デコーダー・エンコーダー・`ImageMetadata` |
| 16 bit・HDR | `ImageBuffer` に画素の形式を足す（エンジン全体にかかわる大きな変更） |
| シャープ・ぼかし・モノクロ・レベル補正・トーンカーブ・ホワイトバランス | フィルター |
| 任意角度の回転（傾き補正） | `EditRecipe` の幾何変換に足す（座標の写像も要る） |
| 範囲選択・文字入れ・描画・レイヤー | 道具と `EditRecipe` の拡張（レイヤーは文書モデルの拡張が要る） |
| 編集内容の保存と `Ctrl+S` | `EditRecipe` は JSON にできるので、ファイルへの保存と読み込みを足す |
| 外部 DLL のプラグインを実行時に読み込む | `AppModules` を、フォルダの DLL を読む形に広げる（署名・信頼の扱いを決めてから） |
| 一括書き出しの並列化 | `BatchExporter` |
| ファイル名の付け方の変更（連番・接頭辞など） | `OutputFileNamer` にテンプレートを足す |
| インストーラー | — |
| webDeza 専用のアイコン | `tools/IconGenerator` に引数を足して出し分ける |

---

## 9. Claude Code への実装指示（要約）

各段階の終わりに `dotnet build CoverCropper.slnx` と `dotnet test CoverCropper.slnx` が成功し、CoverCropper の既存テスト 53 件が書き換えなしで通ることを確かめる。

1. **基準ファイルの作成**: 置き換える前の CoverCropper で、5.5-6 の基準ファイルを作り、テスト用のデータとしてリポジトリに入れる。
2. **エンジン**: `WebDeza.Imaging` を作り、`ImageBuffer`・基本の処理・パラメーター・フィルター（基本補正）・形式の仕組み・編集モデル・`RenderPipeline`・`ExportEncoder`・`SizeTargetSearch`・`EditHistory`・`SafeFileWriter` を実装する。6.1 の E 系のテストを通す。
3. **WIC プラグイン**: `WebDeza.Imaging.Wic` を作り、5.4 の形式を実装する。W 系のテストを通す。
4. **Core と CoverCropper の載せ替え**: `WebDeza.Core` を作り、CoverCropper の画像処理 4 クラスと設定・ログ・ファイル名の 4 クラスの中身を置き換える（5.5）。6.3 の R01〜R05 を確かめる。CoverCropper 設計書の 4.2（ソリューション構成）を今の構成に合わせて直す。
5. **webDeza の土台**: `WebDeza` とテストプロジェクトを作る。`AppInfo`・`AppModules`・`ExportService`・`SettingsService`・`PresetService` と、1 枚の読み込み・全体表示・切り抜き（移動だけ）・書き出し設定・書き出しができる画面を作る。
6. **キャンバスと道具**: `EditorCanvas`・`ViewTransform`・道具の仕組み・切り抜きと手のひらの道具（F-03、F-04）。
7. **編集**: 回転・反転、フィルターのパネル（入力欄の自動生成）、元に戻す / やり直し（F-05〜F-07）。
8. **ファイルサイズの確認**: `ExportPreviewService`・書き出しサイズの表示・書き出しプレビュー・ファイルサイズの上限（F-09）。
9. **一覧と一括書き出し**: `FileCollector`・サムネイル・一覧・`BatchExporter`（F-01 の複数読み込み、F-02、F-11）。
10. **仕上げ**: 拡張の確認（テスト用の形式とフィルターを `AppModules` に足すだけで動くこと）、CI に webDeza の配布用ビルドと成果物（`webDeza-win-x64`）を足す、README に webDeza の説明を足す。
11. 元画像に書き込むコードパス、CoverCropper の設定ファイルに書き込むコードパスが無いことをレビューで確かめる。
