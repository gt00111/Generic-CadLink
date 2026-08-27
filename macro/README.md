# SolidWorksでの実行手順

## 必要ファイル

次のファイルを同じ `macro` フォルダに置きます。

```text
BendExportMacro.swb
BendExportMacro.exe
SolidWorks.Interop.sldworks.dll
SolidWorks.Interop.swconst.dll
cadlink.config.json
```

`.cs` はソースコードであり、SolidWorksから直接実行できません。

## 実行

1. SolidWorks 2022で保存済みの板金パーツを開く
2. **ツール → マクロ → 実行** を選ぶ
3. `BendExportMacro.swb` を選ぶ
4. 完了ダイアログで出力先とエラーを確認する

同名の古い `BendExportMacro.swp` があると、SolidWorksがそちらを開く場合があります。更新後に動作が変わらない場合は、SolidWorksを閉じて古い `.swp` を別名へ退避してください。

## 出力先

```text
{exportRoot}/
  {partNumber}/
    bend.json
    flat.dxf
```

`cadlink.config.json` の `exportRoot` が空の場合は、部品ファイルと同じフォルダの `CadLinkExport` を使います。

## 結果の見方

- `errors: []`：成功
- `BEND_DIRECTION_GEOMETRY_MISMATCH`：SolidWorks方向と独自幾何チェックが不一致。出力ではSolidWorks方向を採用
- `DXF_BEND_COUNT_MISMATCH`：JSON曲げとDXF曲げ線が1対1ではない
- `BEND_DIRECTION_FAILED`：SolidWorks方向を取得または座標照合できない

## 対応範囲

通常の直線曲げ、箱曲げ、Z曲げ、標準的なハット曲げを対象とします。ヘミング、専用ジャグ、成形工具、ロフト・曲線曲げ、マルチボディは対象外です。

## 再ビルド

```powershell
.\scripts\build-macro.ps1
```
