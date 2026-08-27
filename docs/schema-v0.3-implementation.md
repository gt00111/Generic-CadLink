# Schema v0.3 implementation

`src/GenericCadLink.Macro` がschema v0.3の正本実装です。`macro/BendExportMacro.swb` は同じフォルダの `BendExportMacro.exe` を同期起動します。

## 出力契約

```text
{exportRoot}/{partNumber}/
  bend.json
  flat.dxf
```

`bend.json` は以下を含みます。

- mm・右手系・XY展開平面の `coordinateSystem`
- `fixedFace.normal`
- DXFと同じ座標系の `bends[].axis`
- `bends[].signedAngleDeg` と `direction`
- `bends[].movingSidePoint`
- DXF端点・レイヤー・ハンドルを持つ `bends[].dxfLine`

## UP/DOWNの正本

SolidWorksフラットパターン配下の方向付き曲げ線を収集し、スケッチ座標をモデル座標、さらに出力座標へ変換します。各方向線はDXF由来の曲げ軸へ座標で1対1対応させます。

- UP → 正の `signedAngleDeg`、`direction=up`、`BEND_UP`
- DOWN → 負の `signedAngleDeg`、`direction=down`、`BEND_DOWN`

可動側点と曲げ後位置から求める軸回り回転は独立した整合性チェックです。不一致時は `BEND_DIRECTION_GEOMETRY_MISMATCH` を警告として記録し、SolidWorks方向を採用します。

## 厳格な失敗条件

- 座標系または固定面法線を取得できない
- 曲げ軸が欠落またはゼロ長
- SolidWorks曲げ方向を取得・座標照合できない
- 符号付き角度がゼロ
- `direction` とDXFレイヤーが不一致
- `movingSidePoint` が欠落または曲げ軸上
- JSON曲げとDXF曲げ線が1対1ではない
- 解決済み曲げが0本

不足したv0.3必須値は推測で補完しません。

## 対応範囲

完成範囲は保存済みの単一板金パーツで、通常の直線曲げ、箱曲げ、Z曲げ、標準的なハット曲げです。ヘミング、専用ジャグ、成形工具、ロフト・曲線曲げ、マルチボディは別フェーズです。

## ビルドと確認

```powershell
.\scripts\build-macro.ps1
```

SolidWorks 2022で代表モデルを開き、**ツール → マクロ → 実行** から `macro/BendExportMacro.swb` を実行します。`errors` が空で、曲げ本数・方向・角度・DXFレイヤーがSolidWorksと一致することを確認します。
