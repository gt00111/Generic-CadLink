# Generic CadLink

SolidWorks 2022の板金パーツから、板金シミュレーターが再利用できる展開DXFと曲げメタデータを出力するツールです。

## 完成範囲（schema v0.3）

- 保存済みの単一板金パーツを対象
- `bend.json` と `flat.dxf` を品番別フォルダへ出力
- 板厚、SolidWorksマテリアル名、内R、角度、曲げ線長さを出力
- 右手系・mm・XY展開座標を明記
- 曲げ軸、可動側代表点、符号付き角度を出力
- SolidWorksフラットパターンのUP/DOWNを正本として使用
- DXFの `CUT` / `BEND_UP` / `BEND_DOWN` レイヤーを生成
- JSONの各曲げとDXF曲げ線を座標で1対1対応
- 通常曲げ、箱曲げ、Z曲げ、標準的なハット曲げを想定

特殊曲げ（ヘミング、ジャグ専用フィーチャー、成形工具、ロフト・曲線曲げ、マルチボディ）は完成範囲外です。

## 実行方法

1. SolidWorks 2022で保存済みの板金 `.sldprt` を開く
2. **ツール → マクロ → 実行** を選ぶ
3. [`macro/BendExportMacro.swb`](macro/BendExportMacro.swb) を実行する

`BendExportMacro.swb` は同じフォルダの `BendExportMacro.exe` を起動します。次のファイルを同じフォルダに置いてください。

- `BendExportMacro.swb`
- `BendExportMacro.exe`
- `SolidWorks.Interop.sldworks.dll`
- `SolidWorks.Interop.swconst.dll`

詳細は [`macro/README.md`](macro/README.md) を参照してください。

## 出力

`macro/cadlink.config.json` の `exportRoot` が未設定の場合、部品ファイルと同じフォルダの `CadLinkExport` に出力します。

```text
CadLinkExport/
  {partNumber}/
    bend.json
    flat.dxf
```

`exportRoot` を共有フォルダへ変更する例：

```json
{
  "exportRoot": "\\\\server\\CadLinkExport"
}
```

## 曲げ方向

最終的な `direction`、`signedAngleDeg` の符号、DXFレイヤーはSolidWorksフラットパターンの曲げ線方向から決定します。

- UP → `signedAngleDeg > 0` → `BEND_UP`
- DOWN → `signedAngleDeg < 0` → `BEND_DOWN`

可動側の3D幾何から求めた方向は二重チェックに使います。SolidWorks判定と異なる場合は `BEND_DIRECTION_GEOMETRY_MISMATCH` を警告として記録し、SolidWorks判定を採用します。

## 成功条件

- `errors` が空
- SolidWorksとJSONの曲げ本数が一致
- SolidWorks曲げ注記とJSON/DXFのUP/DOWNが一致
- `axis`、`movingSidePoint`、`dxfLine` が各曲げに存在
- JSON曲げとDXF曲げ線が1対1

## ビルド

```powershell
.\scripts\build-macro.ps1
```

出力先：`macro/BendExportMacro.exe`

## 今後の範囲

- .NETアドインUI
- 共有フォルダ監視と自動取込
- M-BEND／portalとの連携
- 特殊曲げのグループ情報と加工工程情報

要件は [`docs/requirements.md`](docs/requirements.md)、v0.3実装詳細は [`docs/schema-v0.3-implementation.md`](docs/schema-v0.3-implementation.md) を参照してください。
