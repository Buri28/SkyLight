# SkyLight
既存Environment（環境）の背景や構造物の色を変更したり非表示にするMODです。  
AIによる試作品です。  
加齢で視力が落ち、暗い背景だとノーツが滲んだりブレて見えないので自分用に作っています。  
ライティングが映えるので暗い背景でプレイしたい気持ちはありますが、ノーツが見えないとゲームとして楽しめないのです。  

今のところ、私と同じような問題で悩んでいる人は聞いたことがなく  
居たとしても、このMODはかなり強引な手法を使っているため   
明るいカスタムプラットフォームを使うことや、カスタムプラットフォーム自体を作ることをお勧めします。  

今のところリリースする予定はありません。(完成度が上がったら考えます)  

## 対応版
- Beat Saber 1.40.8 / 1.44.1（1つの DLL が両方で動きます）。
- 1.44.1 では、manifest の `gameVersion` が 1.40.8 のため、BSIPA が「developed for game version 1.40.8」という警告を出します。読み込み自体はされます。

## 必須 MOD
BSIPA、BeatSaberMarkupLanguage（BSML）、SiraUtil。

## 設定画面
ソロ選曲画面の **MODS** タブ → **SkyLight**。設定はその場で保存され、次の曲から効きます。

| タブ | 内容 |
|---|---|
| Main | MOD の ON/OFF、Bloom、表示/非表示のまとめ（空・床・構造物・リング・ネオン・バー・ライト・ロゴなど）、プリセット、Reset to Defaults |
| Sky / Floor | 空の色・明るさ・不透明度、床の色・明るさ・不透明度、Mirror Reflection |
| Objects | 構造物とリングの色・明るさ・不透明度 |
| Hide | 環境ごとに、非表示にするオブジェクトを選ぶ |

## 保存先
`<Beat Saber のフォルダ>\UserData\` の下です。
- `SkyLight.json`：設定。
- `SkyLight\<名前>.json`：プリセット（設定画面の Presets で保存・読込・削除）。MOD の ON/OFF とログの設定は、プリセットには含まれません。
- `SkyLight\Environments\HideObjects.json`：環境ごとの非表示リストと、見つかったオブジェクトの候補。壊れて読めないときは `.broken` に退避して作り直します。

## ログ
通常は何も出しません（Warn も出しません）。調べるときだけ、`SkyLight.json` の `DebugLogging` を `true` にします（診断ダンプは、さらに `DumpScene` も `true`）。

## 既知の限界
- カスタムプラットフォームの地形は、塗り直せません。
- バー（Spectrogram）の色は変えられません（表示/非表示だけ）。
- ネオン・レーザーは Bloom で光るため、色塗りに向きません（表示/非表示だけ）。
- 全画面の Bloom は画面のアルファで光る量が決まるため、空・床・リングはアルファ 0 で描いています。

## ビルド
`build.bat [Debug|Release] [Plugins フォルダ]`。コピー先を省くと、環境変数 `BeatSaberPath`（無ければ `D:\BSManager\BSInstances\1.40.8`）の `Plugins` に入れます。
依存の DLL は、まずゲーム側（`Beat Saber_Data\Managed`・`Plugins`）から探します。無いものは、環境変数 `BundledDepsDir`（既定は兄弟フォルダの `LRCounter\externals\extracted`）から探します。

## ライセンス
MIT（`LICENSE`）。
