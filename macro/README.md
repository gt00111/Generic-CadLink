# SolidWorks マクロの使い方

SolidWorks の **ツール → マクロ → 実行** で選べるのは **`.swp` / `.swb` / `.dll`** です。  
**`.cs` はソースコードであり、直接実行できません。**

**Phase 2（v0.2.1）** から **`{exportRoot}\{品番}\bend.json` + `flat.dxf`** を出力します。

---

## 出力先（案 A）

```
{exportRoot}\
  Y248-Y100-1002\
    bend.json
    flat.dxf
  Y23Y-Y101-1001\
    bend.json
    flat.dxf
```

| 設定 | 内容 |
|---|---|
| **`macro/cadlink.config.json`** | `exportRoot` … 展開フォルダ（共有パス等） |
| **`exportRoot` が空** | `.sldprt` と同じフォルダ直下の **`CadLinkExport\`** を使用 |
| **品番フォルダ** | ファイル名（拡張子除く）= サブフォルダ名 |

### 本番設定例

`macro/cadlink.config.json`:

```json
{
  "exportRoot": "\\\\server\\CadLinkExport"
}
```

ローカル検証のままなら `"exportRoot": ""` のままで、  
`samples\CadLinkExport\Y248-Y100-1002\` のように出力されます。

---

## 推奨: 初回セットアップ

### 方法 A: `.swb` を直接実行（推奨・参照設定不要）

最新版の `BendExportMacro.swb` は **Late Binding**（`Object` 型）のため、  
**参照設定なし** で **ツール → マクロ → 実行** からそのまま動きます。

1. 板金 `.sldprt` を開いて **保存**
2. **`GenericCadLink.slddxfmap`（または `GenericCadLink.dxfmap`）が `BendExportMacro.swb` と同じ `macro/` フォルダにあること**を確認（レイヤー名 `CUT` / `BEND_UP` / `BEND_DOWN` 用）
3. **ツール → マクロ → 実行**
4. 種類 **SWBasic Macros (*.swb)** → `macro/BendExportMacro.swb`

### 方法 B: `.swp` として保存（従来・任意）

早期バージョンや環境によっては参照設定が必要でした。  
`.swb` で問題が出る場合のみ、以下を実施してください。

1. SolidWorks 2022 を起動
2. **ツール → マクロ → 編集**
3. `macro/BendExportMacro.swb` を開く
4. VBA エディタが開いたら **ツール → 参照設定**
5. 次にチェックを入れる（名称は環境により多少異なります）:
   - **SldWorks … Type Library**（SolidWorks 2022 tlb）
   - **SolidWorks … Constant type library**（定数・`swDocPART` 等）
   - **SolidWorks … Commands type library**（あれば）
6. **OK** → **ファイル → 名前を付けて保存**
7. 保存先例:

   ```
   C:\Users\caduser\Desktop\Generic CadLink\macro\BendExportMacro.swp
   ```

8. 以降は **この `.swp` を実行** する

> **注意**: `.swp` はバイナリのため Git には通常コミットしません。  
> 各 PC で上記 1 回セットアップするか、社内共有フォルダに `.swp` を置いて配布してください。  
> マクロ本体を更新したときは、`.swb` を再度開いて参照設定済みの `.swp` に上書き保存してください。

---

## 日常の実行手順

1. 板金 `.sldprt` を SolidWorks で開く（例: `samples\Stand-01.SLDPRT`）
2. **必ず保存**（未保存だと `UNSAVED_DOCUMENT` エラー）
3. **`macro/GenericCadLink.slddxfmap` がマクロと同じフォルダにあることを確認**
4. **ツール → マクロ → 実行**
5. 種類 **SWBasic Macros (*.swb)** → `macro/BendExportMacro.swb`（または登録済み `.swp`）
6. **`CadLinkExport\{品番}\`**（または設定した `exportRoot\{品番}\`）に `bend.json` と `flat.dxf` が出力される

---

## Phase 2 出力（`flat.dxf`）

| ファイル | 内容 |
|---|---|
| `bend.json` | 曲げメタデータ（Phase 1 同様） |
| `flat.dxf` | 展開図 DXF（mm） |

### レイヤー規約（`GenericCadLink.slddxfmap`）

| SW エンティティ | DXF レイヤー |
|---|---|
| Visible Edges | `CUT` |
| Bend Lines Up | `BEND_UP` |
| Bend Lines Down | `BEND_DOWN` |
| Sketch Entities | `SCRIBE` |

DXF 出力は **ExportToDWG2**（曲げ線を含む）を優先し、出力後に **CUT / BEND_UP / BEND_DOWN** レイヤーへ後処理で割り当てます。  
`ExportFlatPatternView` は ExportToDWG2 失敗時のみ試行します。

### Phase 2 検証チェック

1. DXF を AutoCAD / LibreCAD 等で開き、レイヤー `CUT` / `BEND_UP` / `BEND_DOWN` があること
2. **曲げ線本数 = `bend.json` の `bends[]` 件数**
3. 各曲げの山/谷が JSON `direction` とレイヤー（`BEND_UP` / `BEND_DOWN`）で一致すること

---

## 出力例（Stand-01）

```json
{
  "partNumber": "Stand-01",
  "thickness": 4.5,
  "material": "SS400",
  "bends": [ ... ]
}
```

- 板厚・曲げ R は SW API の **メートル → mm 換算** 済み
- `FLAT_PATTERN_SUPPRESSED` は折りたたみ表示では **警告のみ**（出力は続行）

---

## トラブルシューティング

| 症状 | 対処 |
|---|---|
| エラー 91（オブジェクト未設定） | `.swp` 化 + 参照設定（上記初回セットアップ） |
| コンパイルエラー（ユーザ定義型は定義されていません） | **最新 `.swb` を使用**（Late Binding 版）。古い `.swp` は削除して `.swb` から再実行 |
| 文字化け / `& vbCrLf` がそのまま表示 | 古いマクロを使用中。リポジトリの `.swb` から作り直す（メッセージは英語） |
| 曲げ 0 件 | 最新 `.swb` で `.swp` を再作成。それでも 0 なら SW 上のフィーチャ名を共有 |
| `flat.dxf` が出ない | フラットパターン未生成・抑制を確認。`DXF_EXPORT_FAILED` の詳細を共有 |
| レイヤー名が `CUT` 等にならない | `macro/GenericCadLink.slddxfmap` の配置を確認。`.swb` と同じフォルダ必須 |

---

## ファイルの役割

| ファイル | 用途 |
|---|---|
| **`macro/BendExportMacro.swb`** | ソース（テキスト）。編集 → 参照設定 → `.swp` 保存の元 |
| **`macro/BendExportMacro.swp`** | **実行用**（各 PC で初回セットアップ時に作成） |
| **`macro/cadlink.config.json`** | 出力先 `exportRoot`（展開フォルダ） |
| **`macro/GenericCadLink.slddxfmap`** | DXF レイヤーマップ（Phase 2 必須） |
| **`macro/GenericCadLink.dxfmap`** | 上記の代替ファイル名（互換用） |
| `macro/BendExportMacro.cs` | C# 版ソース（Phase 3 アドイン/DLL 用） |
| `src/GenericCadLink.Macro/` | Visual Studio プロジェクト |

---

## 方法 B: `.dll`（将来・Visual Studio あり）

```powershell
cd "C:\Users\caduser\Desktop\Generic CadLink"
.\scripts\setup-lib.ps1
.\scripts\build-macro.ps1
```

**ツール → マクロ → 実行** → `macro\BendExportMacro.dll`

Phase 1 の検証だけなら **`.swp` で十分** です。

---

## サンプルモデル

| ファイル | 品番 |
|---|---|
| `samples/Stand-01.SLDPRT` | Stand-01 |
| `samples/Y23Y-Y101-1001.SLDPRT` | Y23Y-Y101-1001 |
| `samples/Y248-Y100-1002.SLDPRT` | Y248-Y100-1002 |

3 モデルすべてで `bend.json` が出力されるか確認してください。
