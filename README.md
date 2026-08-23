# Generic CadLink

SolidWorks 2022 板金パーツから portal（板金製造支援）向けに `bend.json` を出力する自作 CADLink。

## Phase 1（現在）

- **成果物**: C# マクロ — 板金曲げメタデータ → `bend.json`
- **実行ファイル**: 初回セットアップ後 **`macro/BendExportMacro.swp`**（元ソース: `macro/BendExportMacro.swb`）
- **出力先**: 開いている `.sldprt` と**同じフォルダ**の `bend.json`
- **加工順**: 出力しない（純正 CadLink → M-BEND と同じ分担。portal 側で決定）

詳細要件: [docs/requirements.md](docs/requirements.md)  
**マクロの登録・実行手順**: [macro/README.md](macro/README.md)

## クイックスタート（SolidWorks）

**日常**（最新 `.swb`）:

1. 板金パーツを開いて **保存**
2. **ツール → マクロ → 実行** → **`macro/BendExportMacro.swb`**
3. 同フォルダに `bend.json` が出力される

> 古い `.swp`（参照設定版）を使っている場合は、最新 **`BendExportMacro.swb`** に切り替えてください。  
> 詳細: [macro/README.md](macro/README.md)

## 前提

- SolidWorks **2022**（64bit）
- .NET Framework **4.8**
- Visual Studio 2019 以降（ビルド用）

## セットアップ

### 1. SolidWorks API DLL を配置

SolidWorks 2022 インストール先から次を `lib\` にコピーします。

```powershell
$sw = "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"
Copy-Item "$sw\api\redist\SolidWorks.Interop.sldworks.dll" "lib\"
Copy-Item "$sw\api\redist\SolidWorks.Interop.swconst.dll" "lib\"
```

パスが異なる場合は環境に合わせて変更してください。

### 2. ビルド

```powershell
msbuild GenericCadLink.sln /p:Configuration=Release
```

出力: `src\GenericCadLink.Macro\bin\Release\GenericCadLink.Macro.dll`

## SolidWorks での実行

**→ [macro/README.md](macro/README.md) を参照（推奨・ビルド不要）**

実行するソース: **`macro/BendExportMacro.cs`**（`Main()` メソッドを含む 1 ファイル）

### 方法 A: マクロとして登録（Phase 1 推奨）

1. SolidWorks で **ツール → マクロ → 新規作成** — C#、名前 **BendExportMacro**
2. `macro/BendExportMacro.cs` の内容をすべて貼り付けて保存
3. 板金 `.sldprt` を開いて保存後、**ツール → マクロ → 実行**

### 方法 B: Visual Studio で DLL ビルド（Phase 3 以降向け）

1. SW マクロプロジェクトに `GenericCadLink.Macro.dll` への参照を追加
2. `Main` 内で:

```csharp
var macro = new GenericCadLink.Macro.BendExportMacro();
macro.swApp = (SldWorks)SwApp;
macro.Main();
```

## 出力例

`ABC-123.sldprt` と同じフォルダに `bend.json`:

```json
{
  "schemaVersion": "0.1",
  "partNumber": "ABC-123",
  "thickness": 1.6,
  "material": "SPCC",
  "bends": [
    {
      "id": "B1",
      "dxfLayer": "BEND_UP",
      "direction": "up",
      "innerRadius": 1.0,
      "angleDeg": 90,
      "lengthMm": 120,
      "swFeatureName": "Edge-Flange1"
    }
  ],
  "errors": [],
  "warnings": []
}
```

## 検証

1. `samples/` に代表 `sldprt` を配置
2. [samples/EXPECTED_TEMPLATE.md](samples/EXPECTED_TEMPLATE.md) に期待値を記入
3. マクロ実行後、SW 画面と `bend.json` の山/谷・R・角度を照合

## プロジェクト構成

```text
Generic CadLink/
  docs/requirements.md
  samples/
  src/GenericCadLink.Macro/   … Phase 1 マクロ
  lib/                        … SW Interop DLL（手動配置）
```

## 次の Phase

| Phase | 内容 |
|---|---|
| 2 | フラットパターン DXF + レイヤー規約 |
| 3 | .NET アドイン（ツールバーボタン） |
| 4 | portal 自動取込 |
