# Generic CadLink 要件定義書

> **版**: v0.4
> **作成日**: 2026-08-21  
> **ステータス**: Phase 2 完了 / schema v0.3 幾何出力を設計中

---

## 1. 背景・目的

### 1.1 背景

- テイクソフト製 **CADLink** は契約していないため、SolidWorks から板金展開データを portal（板金製造支援）へ渡す経路がない。
- portal 側は STEP 形状解析による曲げ検出・M-BEND 風シミュレーション・加工判断エンジンを実装済みだが、**SolidWorks 板金フィーチャ由来の正確な曲げ情報（山/谷・内 R・角度・曲げ線対応）** があれば精度と運用効率が上がる。

### 1.2 目的

**Generic CadLink**（以下 **CadLink**）は、SolidWorks 板金パーツから portal が迷わない **正本データ** を出力する **独立プロジェクト** である。

| やること | やらないこと |
|---|---|
| 展開 DXF（レイヤー規約付き）の出力 | テイクソフト CADLink 互換 |
| 曲げメタデータ JSON（`bend.json`）の出力 | portal 本体の改修（受け口は別途 portal 側） |
| SolidWorks 上のワンクリック／マクロ実行 | 加工判断・金型選定・シミュレーション本体 |
| 品番フォルダへのファイル配置 | アセンブリ全体の一括出力（初期スコープ外） |

### 1.3 成功基準（最小成功）

以下が **品番ごとの出力フォルダ** に生成されること。

```
{exportRoot}\
  {品番}\
    bend.json
    flat.dxf      … Phase 2 以降
  preview.png   … 任意（Phase 1〜2 では出力しない）
```

`exportRoot` は `macro/cadlink.config.json` で指定。未設定時は `.sldprt` 横の `CadLinkExport\`。

portal はこのパッケージを取り込み、既存の `ProcessCondition` / シミュレーション / 判断エンジンへ流す（portal 側は将来 `smsupport:import:bendPackage` 等で実装）。

---

## 2. システム位置づけ

```text
SolidWorks（3D 板金）
    ↓  Generic CadLink（本プロジェクト・別リポジトリ）
    ├─ flat.dxf   … 展開 2D（CUT / BEND_UP / BEND_DOWN 等）
    └─ bend.json  … 曲げメタデータ（正本）
    ↓  ファイル or 共有フォルダ
portal 板金製造支援
    ├─ 展開 3D 表示（将来）
    ├─ M-BEND 風シミュレーション
    └─ 判断エンジン（既存）
```

### 2.1 設計原則

- **依存逆転**: CadLink は portal を import しない。出力はファイルのみ。
- **正本**: 曲げの山/谷・順序の解釈は **JSON が正本**。DXF のレイヤーは **二重チェック用**。
- **疎結合**: portal `STD-008 システム連携標準` に準拠 — 他システム DB を直接更新しない。
- **1 品番 1 部品**: 初期スコープは **板金パーツ 1 ファイル → 1 出力セット**。サブアセンブリ展開は対象外。

---

## 3. ステークホルダー・利用者

| 利用者 | 利用タイミング | 操作 |
|---|---|---|
| 生産技術（SolidWorks 担当） | 板金モデル確定後 | SW 上で「portal へ出力」 |
| 生産技術（portal 担当） | 加工条件入力・シミュ前 | 出力フォルダから portal 取込（将来自動） |
| 現場 | 参照のみ | portal 上の加工条件・シミュ結果 |

---

## 4. 入出力仕様

### 4.1 入力

| 項目 | 内容 |
|---|---|
| SolidWorks 板金パーツ | `.sldprt`（Sheet Metal フィーチャあり） |
| フラットパターン | 生成済み・最新状態であること |
| 品番 | **ファイル名**（拡張子 `.sldprt` を除く） |
| 材質 | **SolidWorks マテリアル名**（`Part` の材料プロパティ） |

### 4.2 出力ファイル

#### 4.2.1 `bend.json`（必須・正本）

M-BEND で曲げ回転を一意に再現する新しい正本仕様は
[`bend-package-schema-v0.3.md`](bend-package-schema-v0.3.md) とする。
従来 schema v0.1 の `direction` は表示・旧連携用であり、決定論的な
3D 曲げ再現には使用しない。

schema v0.3 では以下を必須とする。

- 右手系の展開座標系と `flatNormal`
- `fixedFace.normal`（`outer/inner` 不明でも必須）
- 各曲げの決定論的に向きを固定した3D軸
- 実幾何から計算した `signedAngleDeg`
- 可動側を示す `movingSidePoint`
- JSON 曲げと DXF 曲げ線の1対1対応
- `signedAngleDeg` から一意に派生した `direction` と `dxfLayer`

取得不能時に `up` を仮定して成功扱いにすることを禁止する。

**レガシースキーマ v0.1**（既存マクロ互換・M-BEND の3D再現には使用不可）:

```json
{
  "schemaVersion": "0.1",
  "exportedAt": "2026-08-21T14:30:00+09:00",
  "source": {
    "cad": "SolidWorks",
    "cadVersion": "2022",
    "cadLinkVersion": "0.1.0",
    "fileName": "ABC-123.sldprt",
    "filePath": "C:\\Projects\\ABC-123.sldprt"
  },
  "partNumber": "ABC-123",
  "revision": null,
  "thickness": 1.6,
  "material": "SPCC",
  "fixedFace": null,
  "units": "mm",
  "coordinateSystem": {
    "origin": "flatPattern",
    "description": "フラットパターン展開面の左下（要決定 §12）"
  },
  "bends": [
    {
      "id": "B1",
      "dxfLayer": "BEND_UP",
      "dxfEntityHandle": "A1B2",
      "direction": "up",
      "innerRadius": 1.0,
      "angleDeg": 90,
      "lengthMm": 120,
      "swFeatureName": "Sheet-Metal1",
      "axisStart": [0, 0, 0],
      "axisEnd": [120, 0, 0]
    }
  ],
  "errors": [],
  "warnings": []
}
```

**フィールド定義**:

| フィールド | 必須 | 型 | 説明 | portal 連携 |
|---|---|---|---|---|
| `partNumber` | ○ | string | 品番 | `ProcessCondition.partNumber` 突合 |
| `thickness` | ○ | number | 板厚 (mm) | `ProcessCondition.thickness` |
| `material` | △ | string | 材質名（SW マテリアル名） | `ProcessCondition.material` |
| `fixedFace` | ○ (v0.3) | object | 法線必須、side 判定状態を分離 | M-BEND の回転基準 |
| `bends[].id` | ○ | string | 曲げ ID（`B1`…） | DXF 線との対応キー |
| `bends[].direction` | ○ | `"up"` \| `"down"` | `signedAngleDeg` から派生 | M-BEND の表示・検証 |
| `bends[].signedAngleDeg` | ○ (v0.3) | number | 曲げ軸まわり右ねじ方向を正 | M-BEND の回転量 |
| `bends[].axis` | ○ (v0.3) | object | start/end/direction の3D軸 | M-BEND の回転軸 |
| `bends[].movingSidePoint` | ○ (v0.3) | number[3] | 展開状態の可動側代表点 | 回転対象側の決定 |
| `bends[].innerRadius` | ○ | number | 内 R (mm) | `DetectedBend.innerRadius` |
| `bends[].angleDeg` | ○ | number | 曲げ角度 (°) | `DetectedBend.angleDeg` |
| `bends[].lengthMm` | △ | number | 曲げ線長 (mm) | `DetectedBend.lengthMm` |
| `bends[].swFeatureName` | △ | string | SW 曲げフィーチャ名（識別用） | 曲げ対応付けの参考 |
| `bends[].dxfLayer` | ○ | string | DXF レイヤー名 | 二重チェック |
| `bends[].dxfEntityHandle` | △ | string | DXF エンティティ ID | DXF↔JSON 突合 |
| `errors[]` | - | string[] | 致命エラー | 取込拒否理由 |
| `warnings[]` | - | string[] | 警告 | UI 表示用 |

**direction の定義（schema v0.3）**:

| 値 | 意味 | DXF レイヤー |
|---|---|---|
| `up` | `signedAngleDeg > 0` | `BEND_UP` |
| `down` | `signedAngleDeg < 0` | `BEND_DOWN` |

SolidWorks の `BendDirection` / `BendDown` 列挙値は診断用に保持してよいが、
schema v0.3 の符号決定には使用しない。符号は展開基準面法線、決定論的な
曲げ軸方向、曲げ後の可動面法線を同一の出力座標系へ変換して計算する。

**曲げ順（加工順）の扱い** 【決定 2026-08-21・修正】:

テイクソフト純正 CADLink の役割分担に合わせる（[M-BEND 製品ページ](https://takesoft.com/products/prod_bend.html) より）。

| 製品 | 曲げ順の責務 |
|---|---|
| **CADLink**（SW アドイン） | 3D モデル / 展開 DXF + **曲げ幾何メタ**（角度・R・山/谷・板厚・材質）を M-BEND へ渡す |
| **M-BEND**（外部 CAM） | **金型選定・曲げ順検索・干渉チェック** — 「金型・曲げ順同時検索」 |
| **Generic CadLink**（本プロジェクト） | 純正 CadLink と同様 — **加工順（sequence）は出力しない** |
| **portal**（板金製造支援） | M-BEND に相当 — 既存 `planSequence` / 判断エンジンで曲げ順を決定 |

- `bend.json` の `bends[]` は **曲げの一覧（幾何情報）** であり、配列順は SW フィーチャ走査順など **安定した識別順** とする（加工順ではない）。
- **加工順** は portal 取込後に `bend-sequence.engine` 等で生成し、`ProcessConditionBend.bendSequence` に反映する。
- 将来、純正と異なる仕様が判明した場合は §12 Q11 を再検討する。

#### 4.2.2 `flat.dxf`（必須）

| レイヤー | 内容 | 必須 |
|---|---|---|
| `CUT` / `OUTLINE` | 外周・穴・切欠き | ○ |
| `BEND_UP` | 山折り曲げ線 | ○（該当時） |
| `BEND_DOWN` | 谷折り曲げ線 | ○（該当時） |
| `SCRIBE` | スクリーブ線 | - |

- **単位**: mm
- **原点・向き**: JSON `coordinateSystem` と一致させる（portal 2D 表示追加時に必要）
- 曲げ線は **1 曲げ = 1 LINE（または同等）** とし、`bend.json` の `id` / `dxfEntityHandle` で対応付ける

#### 4.2.3 `preview.png`（任意・Phase 1〜2 では対象外）

| 用途 | 説明 |
|---|---|
| 目視確認 | SW を開かずに展開形状をざっと確認（メール添付・チャット共有） |
| portal サムネ | 品番検索一覧のサムネイル（**未実装・将来検討**） |
| 取込前チェック | portal 取込 UI で DXF のプレビュー表示 |

**Phase 1〜2 では不要**。`bend.json` と SW 画面での照合で検証可能。Phase 3 以降、運用上「出力結果を SW 外で確認したい」ニーズが出た場合に追加検討する。

### 4.3 出力先

| 項目 | 内容 |
|---|---|
| 本番パス | **未決定** — 挙動確認後に `cadlink.config.json` 等へ反映 |
| Phase 1 既定 | **`{exportRoot}\{品番}\`** に `bend.json` + `flat.dxf`（`macro/cadlink.config.json` の `exportRoot`） |
| `exportRoot` 未設定 | `.sldprt` 同フォルダの **`CadLinkExport\`** を展開フォルダとして使用 |
| 将来 | Phase 3 アドイン UI から出力先変更 |
| 上書き | 既存ファイルは上書き（`exportedAt` で判別） |

```text
C:\Projects\ABC-123.sldprt              … 入力
\\server\CadLinkExport\ABC-123\        … 出力（exportRoot + 品番）
  bend.json
  flat.dxf
```

---

## 5. SolidWorks API 要件

### 5.1 参照 API（調査済み候補）

| API / インターフェース | 用途 |
|---|---|
| `IFeatureManager` | フィーチャツリー走査 |
| Sheet Metal bends / `IBends` | 曲げ一覧 |
| `IFlatPatternFeature` | フラットパターン状態確認 |
| `BendDirection` | 山/谷判定 |
| Export DXF | 展開 DXF 出力 |

### 5.2 前提条件・エラー

以下の場合は **出力せず** `errors[]` に記録する（または部分出力 + エラー）:

| 条件 | エラーコード（案） | メッセージ例 |
|---|---|---|
| 板金パーツでない | `NOT_SHEET_METAL` | 板金フィーチャがありません |
| フラットパターン未生成 | `NO_FLAT_PATTERN` | フラットパターンを更新してください |
| フラットパターンが抑制 | `FLAT_PATTERN_SUPPRESSED` | フラットパターンが抑制されています |
| 板厚取得不可 | `THICKNESS_UNKNOWN` | 板厚を取得できません |
| 曲げ 0 件 | `NO_BENDS` | 曲げフィーチャがありません（警告扱いも可） |

※ 品番はファイル名から自動取得するため `PART_NUMBER_MISSING` は原則発生しない（空ファイル名のみ例外）。

---

## 6. 実装フェーズ

| Phase | 内容 | 成果物 | 完了条件 |
|---|---|---|---|
| **1** | SW マクロ（VBA または C#）で板金フィーチャ → `bend.json` 出力 | マクロ + サンプル JSON | **山/谷が SW API から取得できる** ことを実機検証 |
| **2** | フラットパターン DXF 出力 + レイヤー規約 | `flat.dxf` + レイヤー割当 | portal 側 DXF パーサで読める |
| **3** | .NET アドイン（「portal へ出力」ボタン） | 署名済みアドイン | 運用可能な UI |
| **4** | 品番連携・フォルダ監視・portal 自動取込 | 本番運用 | 手動取込不要 |

**推奨着手**: Phase 1 のみで API 可否と JSON スキーマを固める。

---

## 7. 技術スタック

| 方式 | メリット | デメリット | 用途 |
|---|---|---|---|
| VBA / C# マクロ | 早い・配布簡単 | UI 弱い | Phase 1 検証 |
| C# アドイン（SW API） | 本番向き | 署名・年次バージョン対応 | Phase 3 以降 |
| Document Manager API | バッチ向き | 板金詳細は本体 API の方が楽 | 将来バッチ |

- **言語**: C#（.NET Framework 4.8 想定 — SW アドイン慣行）
- **対象 SW バージョン**: **SolidWorks 2022**（API 参照 DLL 固定。他バージョン対応は将来）
- **配布**: アドイン `.dll` + インストーラ or 手順書

---

## 8. portal 連携（将来・portal 側タスク）

CadLink 完成後、portal 側で以下を実装する（**本プロジェクトのスコープ外**）:

| 項目 | 内容 |
|---|---|
| IPC | `smsupport:import:bendPackage` — DXF + JSON を品番に紐付け |
| 取込 | `ProcessCondition` へ `thickness` / `material` / 曲げ一覧を反映 |
| 曲げ対応 | `detectedBendIndex` ↔ CadLink `bends[].id` のマッピング |
| 2D 表示 | 展開 DXF の viewer（将来） |
| 自動取込 | 共有フォルダ監視 or ポーリング |

### 8.1 portal 既存型とのマッピング

| CadLink | portal 型 | 備考 |
|---|---|---|
| `bends[].innerRadius` | `DetectedBend.innerRadius` | 直接対応 |
| `bends[].angleDeg` | `DetectedBend.angleDeg` | 直接対応 |
| `bends[].lengthMm` | `DetectedBend.lengthMm` | 直接対応 |
| `bends[].direction` | （新規） | portal STEP 解析には山/谷なし — **CadLink 取込時の付加価値** |
| 加工順 | `ProcessConditionBend.bendSequence` | **portal 側で生成**（CadLink は出力しない） |
| `thickness` | `ProcessCondition.thickness` | 直接対応 |
| `material` | `ProcessCondition.material` | マスタ名との正規化要 |

---

## 9. 非機能要件

| 項目 | 要件 |
|---|---|
| 性能 | 単品番出力 30 秒以内（目標） |
| 信頼性 | SW クラッシュ時も SW 本体は復帰可能 |
| ログ | 出力成否・エラー内容をログファイルに記録 |
| セキュリティ | 共有フォルダは社内 LAN のみ。認証情報は CadLink に持たない |
| 保守 | SW メジャーアップデート時に API 互換確認 |

---

## 10. 既知の注意点・制約

1. **テイクソフト CADLink 互換は不要** — 自社スキーマでよい。
2. **展開寸法**: SW 展開設定（K-factor / ベンド許容）と portal の伸び DB は **後で揃える**。CadLink は SW の展開結果をそのまま出す。
3. **曲げ順**: **純正 CadLink と同じ** — CadLink は加工順を決めない。portal（M-BEND 相当）の `planSequence` が担当。
4. **材質名**: **SW マテリアル名**をそのまま出力。portal 材質マスタとの表記ゆれは取込時に正規化。
5. **特殊曲げ**（ヘミング・ジャグ等）: **現時点では対象外**。通常の `Sheet Metal Bend` のみ。
6. **マルチボディ板金**: 初期スコープ外。1 ボディ 1 出力から開始。
7. **アセンブリ**: 品番 1 部品 1 出力。アセンブリ一括は将来。
8. **図面 Rev**: portal は Rev 管理あり — JSON に `revision` を載せる方針（取得元要決定）。

---

## 11. 受け入れ基準（Phase ごと）

### Phase 1

- [ ] 代表パーツ 3 件以上で `bend.json` が出力される
- [ ] 全曲げについて `direction`（山/谷）が SW 画面と一致する
- [ ] `innerRadius` / `angleDeg` / `material` が SW と一致する
- [ ] `bends[]` に **加工順（sequence）フィールドが含まれない**（純正 CadLink 同等）
- [ ] 非板金・フラット未更新時に `errors[]` が返る
- [ ] 提供いただく代表 `sldprt` で検証完了

### Phase 2

- [ ] `flat.dxf` が指定レイヤーで出力される
- [ ] 曲げ線本数 = `bends[]` 件数
- [ ] DXF 曲げ線と JSON `id` が 1:1 対応

### Phase 3

- [ ] SW ツールバーから 1 クリックで出力完了
- [ ] 出力先を設定画面で変更可能

### Phase 4

- [ ] portal 側取込で `ProcessCondition` が自動生成される（portal タスク）

---

## 12. 決定事項・未確定事項

### 12.0 決定済み

| # | 項目 | 決定内容 |
|---|---|---|
| Q1 | SolidWorks バージョン | **2022** |
| Q4 | 品番取得元 | **ファイル名**（`.sldprt` 除く） |
| Q6 | 材質取得元 | **SW マテリアル名** |
| Q7 | 出力先 | **`{exportRoot}\{品番}\`** — `macro/cadlink.config.json` の `exportRoot`（空なら `CadLinkExport\`） |
| Q8 | preview.png | **Phase 1〜2 では不要**（§4.2.3 参照） |
| Q10 | fixedFace | **社内固定なし** — SW から取れれば記録 |
| Q11 | 曲げ順 | **純正 CadLink 同等** — CadLink は加工順を出さず **portal が決定** |
| Q12 | 特殊曲げ | **現時点対象外**（通常 Bend のみ） |
| Q17 | テストデータ | **代表 sldprt を提供可能** |

### 12.1 SolidWorks 環境（未決定）

| # | 質問 | 選択肢 / 備考 |
|---|---|---|
| Q1 | ~~対象 SolidWorks バージョン~~ | ✅ **2022** |
| Q2 | **Sheet Metal ライセンス**は全席にあるか？ | Professional 以上 + 板金アドイン |
| Q3 | 利用 **席数・配布方法**は？ | 全生技 / 特定 PC のみ / マクロ配布可か |

### 12.2 品番・メタデータ（未決定）

| # | 質問 | 選択肢 / 備考 |
|---|---|---|
| Q4 | ~~品番（partNumber）の取得元~~ | ✅ **ファイル名** |
| Q5 | **Rev** は SW から取るか？ | カスタムプロパティ / ファイル名 / 不要 |
| Q6 | ~~材質の取得元~~ | ✅ **SW マテリアル名** |

### 12.3 出力・運用（未決定）

| # | 質問 | 選択肢 / 備考 |
|---|---|---|
| Q7 | ~~出力先~~ | ⏳ 本番経路未決定 / Phase 1 は **同フォルダ直接出力** |
| Q8 | ~~preview.png~~ | ✅ **Phase 1〜2 不要** |
| Q9 | 出力時 **SW を開いたまま** か **バッチ（未開き）** も必要か？ | 初期は開いたパーツのみで可か |

### 12.4 曲げ・幾何（未決定）

| # | 質問 | 選択肢 / 備考 |
|---|---|---|
| Q10 | ~~fixedFace（展開基準）~~ | ✅ **固定ルールなし** |
| Q11 | ~~曲げ順~~ | ✅ **純正 CadLink 同等**（portal が加工順を決定） |
| Q12 | ~~特殊曲げ~~ | ✅ **現時点対象外** |
| Q13 | **曲げ線が SW 上で分割** されているケースはあるか？ | 1 曲げ 1 線にマージするか |

### 12.5 portal 連携（未決定）

| # | 質問 | 選択肢 / 備考 |
|---|---|---|
| Q14 | portal 取込の **優先タイミング** は？ | CadLink Phase 2 完了後 / Phase 4 と同時 |
| Q15 | 取込時 **既存 ProcessCondition がある** 場合の扱いは？ | 上書き / マージ / 確認ダイアログ |
| Q16 | STEP モデル（既存 `simulations`）と **CadLink データの優先** は？ | CadLink 正 / STEP 解析正 / 項目ごと |

### 12.6 代表テストデータ

| # | 質問 | 状態 |
|---|---|---|
| Q17 | Phase 1 検証用の **代表 sldprt** | ✅ **提供可能** — `samples/` 配下へ配置予定 |
| Q18 | 正解データ（期待値） | 下記 **§15 テスト期待値テンプレ** を参照 |

---

## 15. Phase 1 検証用・期待値データ（Q18）

マクロ出力が正しいか自動／手動で照合するため、代表 `sldprt` **1 ファイルにつき 1 表** を用意してください。Excel / CSV / Markdown どれでも構いません。

### 15.1 部品共通（1 行）

| 項目 | 例 | 取得方法 |
|---|---|---|
| ファイル名 | `ABC-123.sldprt` | — |
| 品番（期待） | `ABC-123` | ファイル名 |
| 板厚 (mm) | `1.6` | SW 板金プロパティ |
| 材質 | `SPCC` | SW マテリアル名 |
| 曲げ本数 | `3` | SW 曲げフィーチャ数 |

### 15.2 曲げごと（曲げの数だけ行を追加）

SW の **フィーチャマネージャ** または **板金の曲げ一覧** を見ながら、次の列を埋めてください。

| # | SW フィーチャ名 | 山/谷 | 内 R (mm) | 角度 (°) | 曲げ線長 (mm) | メモ |
|---|---|---|---|---|---|---|
| 1 | `Sheet-Metal1` | 山 | 1.0 | 90 | 120 | L 字の短辺 |
| 2 | `Sheet-Metal2` | 谷 | 1.0 | 90 | 80 | |
| 3 | … | … | … | … | … | |

**山/谷の見方（SW 2022）**:

- 3D 表示でフラットパターンを解除し、各曲げの **曲げ方向（Bend Direction）** アイコン／プロパティを確認
- 「上向き」「下向き」等の SW 表示を **山 / 谷** に変換して記載（初回はスクショ添付があると確実）

**曲げ線長**: 曲げエッジの長さ（mm）。SW 曲げプロパティまたは寸法で確認。不明なら空欄可（Phase 1 では `lengthMm` は推奨項目）。

**加工順は不要** — 純正 CadLink 同様、期待値表にも加工順列は含めない。

### 15.3 用意があると助かるもの（任意）

- フラットパターン表示の **スクリーンショット**（曲げ方向が分かるもの）
- 板厚・内 R が分かる **フィーチャプロパティ** のスクショ
- 「この部品で困りそうな点」のメモ（例: 曲げが 1 本だけ逆に見える等）

### 15.4 配置場所

```text
samples/
  ABC-123.sldprt
  ABC-123.expected.md    … 上記テンプレに沿った期待値（任意）
```

期待値ファイルがなくても Phase 1 は開始可能ですが、**1 部品分でも** あると山/谷の取り違えを早期に防げます。

---

## 13. 用語集

| 用語 | 説明 |
|---|---|
| 山折り（up） | 固定面から見てフランジが上方向に曲がる |
| 谷折り（down） | 固定面から見てフランジが下方向に曲がる |
| フラットパターン | SW 板金の展開状態 |
| 正本 | システム間で信頼する唯一のデータ源（本件では `bend.json`） |
| portal | 社内 Electron デスクトップアプリ（板金製造支援モジュール含む） |

---

## 14. 変更履歴

| 日付 | 版 | 内容 |
|---|---|---|
| 2026-08-21 | 0.1 | 初版ドラフト（参考資料 + portal 既存型を反映） |
| 2026-08-21 | 0.2 | Q1/Q4/Q7/Q10/Q11/Q17 の回答を反映。Phase 1 着手可能に |
| 2026-08-21 | 0.3 | Q11 を純正 CadLink 仕様に修正。Q6/Q7/Q8/Q12 確定。§15 期待値テンプレ追加 |
| 2026-08-21 | 0.3.1 | Phase 1 マクロ動作確認。運用手順: .swb → 参照設定 → .swp で実行 |
