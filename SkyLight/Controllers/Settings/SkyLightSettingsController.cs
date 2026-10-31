using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.ViewControllers;
using SkyLight.Configuration;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Zenject;

namespace SkyLight.Controllers.Settings
{
    /// <summary>
    /// ソロメニューの MODS タブに表示する SkyLight の設定画面。BSML を使用します。
    /// </summary>
    [ViewDefinition(SkyLightSettingsController.ResourceName)]
#if DEBUG
    // 開発用のホットリロード（ビルドした環境のパスが DLL に入るので、Release には入れない）
    [HotReload(RelativePathToLayout = @"..\Views\SkyLightSettingsView.bsml")]
#endif
    public class SkyLightSettingsController : BSMLAutomaticViewController
    {
        // 埋め込みリソースの BSML。MODS タブ（SkyLightMenuManager）と ViewDefinition で同じ名前を使う
        public const string ResourceName = "SkyLight.Views.SkyLightSettingsView.bsml";

        private PluginConfig _config = null!;

        // json の色が読めないときに使う色。PluginConfig の既定値を正とする（別の値を二重に持たない）
        private static readonly PluginConfig Defaults = new PluginConfig();
        private static Color DefaultColor(string hex) => ColorUtil.ParseHex(hex, Color.white);

        [Inject]
        public void Construct(PluginConfig config)
        {
            _config = config;
        }

        [UIValue("enabled")]
        public bool Enabled
        {
            get => _config.Enabled;
            set { _config.Enabled = value; _config.Changed(); }
        }

        // ─── 背景 ─────────────────────────────────────────────────
        [UIValue("recolor-background")]
        public bool RecolorBackground
        {
            get => _config.RecolorBackground;
            // 同じ値をShow/Hideまとめと[Background]の2か所に表示しているため、変更時は再通知してもう一方も同期する。
            set { _config.RecolorBackground = value; _config.Changed(); NotifyPropertyChanged(); }
        }

        [UIValue("background-color")]
        public Color BackgroundColor
        {
            get => ColorUtil.ParseHex(_config.BackgroundColor, DefaultColor(Defaults.BackgroundColor));
            set { _config.BackgroundColor = ColorUtil.ToHex(value); _config.Changed(); }
        }

        [UIValue("background-brightness")]
        public float BackgroundBrightness
        {
            get => _config.BackgroundBrightness;
            set { _config.BackgroundBrightness = value; _config.Changed(); }
        }

        [UIValue("background-alpha")]
        public float BackgroundAlpha
        {
            get => _config.BackgroundAlpha;
            set { _config.BackgroundAlpha = value; _config.Changed(); }
        }

        [UIValue("paint-floor")]
        public bool PaintFloor
        {
            get => _config.PaintFloor;
            set { _config.PaintFloor = value; _config.Changed(); }
        }

        [UIValue("show-floor")]
        public bool ShowFloor
        {
            get => _config.ShowFloor;
            // Show/Hideまとめと[Floor]の2か所表示のため再通知で同期。
            set { _config.ShowFloor = value; _config.Changed(); NotifyPropertyChanged(); }
        }

        [UIValue("show-side-lanes")]
        public bool ShowSideLanes
        {
            get => _config.ShowSideLanes;
            set { _config.ShowSideLanes = value; _config.Changed(); }
        }

        [UIValue("show-lights")]
        public bool ShowLights
        {
            get => _config.ShowLights;
            set { _config.ShowLights = value; _config.Changed(); }
        }

        [UIValue("show-logo")]
        public bool ShowLogo
        {
            get => _config.ShowLogo;
            set { _config.ShowLogo = value; _config.Changed(); }
        }

        [UIValue("show-hud-background")]
        public bool ShowHudBackground
        {
            get => _config.ShowHudBackground;
            set { _config.ShowHudBackground = value; _config.Changed(); }
        }

        [UIValue("show-other-structures")]
        public bool ShowOtherStructures
        {
            get => _config.ShowOtherStructures;
            set { _config.ShowOtherStructures = value; _config.Changed(); }
        }

        [UIValue("floor-color")]
        public Color FloorColor
        {
            get => ColorUtil.ParseHex(_config.FloorColor, DefaultColor(Defaults.FloorColor));
            set { _config.FloorColor = ColorUtil.ToHex(value); _config.Changed(); }
        }

        [UIValue("floor-brightness")]
        public float FloorBrightness
        {
            get => _config.FloorBrightness;
            set { _config.FloorBrightness = value; _config.Changed(); }
        }

        [UIValue("floor-alpha")]
        public float FloorAlpha
        {
            get => _config.FloorAlpha;
            set { _config.FloorAlpha = value; _config.Changed(); }
        }

        [UIValue("paint-structures")]
        public bool PaintStructures
        {
            get => _config.PaintStructures;
            set { _config.PaintStructures = value; _config.Changed(); }
        }

        [UIValue("show-structures")]
        public bool ShowStructures
        {
            get => _config.ShowStructures;
            // Show/Hideまとめと[Structures]の2か所表示のため再通知で同期。
            set { _config.ShowStructures = value; _config.Changed(); NotifyPropertyChanged(); }
        }

        [UIValue("structure-colorize")]
        public bool StructureColorize
        {
            get => _config.StructureColorize;
            set { _config.StructureColorize = value; _config.Changed(); }
        }

        [UIValue("structure-color")]
        public Color StructureColor
        {
            get => ColorUtil.ParseHex(_config.StructureColor, DefaultColor(Defaults.StructureColor));
            set { _config.StructureColor = ColorUtil.ToHex(value); _config.Changed(); }
        }

        [UIValue("structure-brightness")]
        public float StructureBrightness
        {
            get => _config.StructureBrightness;
            set { _config.StructureBrightness = value; _config.Changed(); }
        }

        [UIValue("structure-alpha")]
        public float StructureAlpha
        {
            get => _config.StructureAlpha;
            set { _config.StructureAlpha = value; _config.Changed(); }
        }

        [UIValue("show-bars")]
        public bool ShowBars
        {
            get => _config.ShowBars;
            set { _config.ShowBars = value; _config.Changed(); }
        }

        [UIValue("bloom")]
        public bool Bloom
        {
            get => _config.Bloom;
            set { _config.Bloom = value; _config.Changed(); }
        }

        [UIValue("show-neon")]
        public bool ShowNeon
        {
            get => !_config.HideNeon;
            set { _config.HideNeon = !value; _config.Changed(); }
        }

        [UIValue("mirror-reflection")]
        public bool MirrorReflection
        {
            get => _config.MirrorReflection;
            set { _config.MirrorReflection = value; _config.Changed(); }
        }

        [UIValue("show-custom-background")]
        public bool ShowCustomBackground
        {
            get => _config.ShowCustomBackground;
            set { _config.ShowCustomBackground = value; _config.Changed(); }
        }

        // 全環境共通の HideObjectHints / HideObjectExcludeHints は画面に出さない（VR のソフトキーボードで長い名前を
        // 打つことはまず無く、環境ごとの欄で足りるため）。SkyLight.json を直接書けば今までどおり効く。

        [UIValue("paint-ring")]
        public bool PaintRing
        {
            get => _config.PaintRing;
            set { _config.PaintRing = value; _config.Changed(); }
        }

        [UIValue("show-ring")]
        public bool ShowRing
        {
            get => _config.ShowRing;
            // Show/Hideまとめと[Ring]の2か所表示のため再通知で同期。
            set { _config.ShowRing = value; _config.Changed(); NotifyPropertyChanged(); }
        }

        [UIValue("ring-colorize")]
        public bool RingColorize
        {
            get => _config.RingColorize;
            set { _config.RingColorize = value; _config.Changed(); }
        }

        [UIValue("ring-color")]
        public Color RingColor
        {
            get => ColorUtil.ParseHex(_config.RingColor, DefaultColor(Defaults.RingColor));
            set { _config.RingColor = ColorUtil.ToHex(value); _config.Changed(); }
        }

        [UIValue("ring-brightness")]
        public float RingBrightness
        {
            get => _config.RingBrightness;
            set { _config.RingBrightness = value; _config.Changed(); }
        }

        [UIValue("ring-alpha")]
        public float RingAlpha
        {
            get => _config.RingAlpha;
            set { _config.RingAlpha = value; _config.Changed(); }
        }

        // ───── Reset ─────
        [UIAction("reset-settings")]
        public void ResetSettings()
        {
            // PluginConfig の初期化子を単一の正として、プリセットの対象の項目を全部（画面に出さない hint 類も）既定値へ戻す。
            // Enabled・DebugLogging・DumpScene は設定の中身ではないので戻さない（PresetManager.NotPreset）。
            PresetManager.ResetToDefaults(_config);
            _config.Changed();
            NotifyAllUi();
        }

        // 全 UI バインドプロパティの再通知（Reset / プリセット読込の後に画面へ反映する）。
        private void NotifyAllUi()
        {
            NotifyPropertyChanged(nameof(Enabled));
            NotifyPropertyChanged(nameof(RecolorBackground));
            NotifyPropertyChanged(nameof(BackgroundColor));
            NotifyPropertyChanged(nameof(BackgroundBrightness));
            NotifyPropertyChanged(nameof(BackgroundAlpha));
            NotifyPropertyChanged(nameof(Bloom));
            NotifyPropertyChanged(nameof(ShowNeon));
            NotifyPropertyChanged(nameof(ShowCustomBackground));
            NotifyPropertyChanged(nameof(MirrorReflection));
            NotifyPropertyChanged(nameof(ShowFloor));
            NotifyPropertyChanged(nameof(ShowSideLanes));
            NotifyPropertyChanged(nameof(ShowLights));
            NotifyPropertyChanged(nameof(ShowLogo));
            NotifyPropertyChanged(nameof(ShowOtherStructures));
            NotifyPropertyChanged(nameof(ShowHudBackground));
            NotifyPropertyChanged(nameof(PaintFloor));
            NotifyPropertyChanged(nameof(FloorColor));
            NotifyPropertyChanged(nameof(FloorBrightness));
            NotifyPropertyChanged(nameof(FloorAlpha));
            NotifyPropertyChanged(nameof(ShowStructures));
            NotifyPropertyChanged(nameof(PaintStructures));
            NotifyPropertyChanged(nameof(StructureColorize));
            NotifyPropertyChanged(nameof(StructureColor));
            NotifyPropertyChanged(nameof(StructureBrightness));
            NotifyPropertyChanged(nameof(StructureAlpha));
            NotifyPropertyChanged(nameof(ShowBars));
            NotifyPropertyChanged(nameof(ShowRing));
            NotifyPropertyChanged(nameof(PaintRing));
            NotifyPropertyChanged(nameof(RingColorize));
            NotifyPropertyChanged(nameof(RingColor));
            NotifyPropertyChanged(nameof(RingBrightness));
            NotifyPropertyChanged(nameof(RingAlpha));
        }

        // ───── Presets ─────────────────────────────────────────────
        // プリセットは UserData/SkyLight/<name>.json として保存される。
        private const string NoPreset = "—";

        [UIComponent("preset-dropdown")]
        private DropDownListSetting _presetDropdown = null!;

        // ドロップダウンの選択肢（保存済みプリセット名）。空のときはプレースホルダ1件。
        [UIValue("preset-options")]
        public List<object> PresetOptions { get; private set; } = new List<object> { NoPreset };

        // 現在選択中のプリセット名。
        [UIValue("preset-selected")]
        public string PresetSelected { get; set; } = NoPreset;

        // 新規保存名（空なら選択中の名前へ上書き保存）。
        [UIValue("preset-name")]
        public string PresetName { get; set; } = "";

        // 画面構築後にドロップダウンの選択肢を実ファイルから組み立てる。
        // BSML は同じ名前の UIAction を1つしか持てないので、画面の初期化はここにまとめる。
        [UIAction("#post-parse")]
        private void PostParse()
        {
            RebuildPresetOptions(PresetSelected);
            RebuildEnvOptions(EnvironmentHideList.LastPlayed);
        }

        // 保存済みファイルから選択肢を作り直し、ドロップダウンへ反映する。
        private void RebuildPresetOptions(string? select)
        {
            var names = PresetManager.List();
            PresetOptions = names.Count > 0
                ? names.Cast<object>().ToList()
                : new List<object> { NoPreset };

            // 大文字小文字だけ違う名前でも、同じファイル（Windows）なので、同じものを選ぶ
            var hit = string.IsNullOrEmpty(select) ? null : names.FirstOrDefault(n => string.Equals(n, select, System.StringComparison.OrdinalIgnoreCase));
            if (hit != null)
                PresetSelected = hit;
            else if (!PresetOptions.Contains(PresetSelected))
                PresetSelected = (string)PresetOptions[0];

            if (_presetDropdown != null)
            {
                _presetDropdown.Values = PresetOptions;
                _presetDropdown.Value = PresetSelected;
                _presetDropdown.UpdateChoices();
                _presetDropdown.ReceiveValue();
            }
        }

        // ドロップダウンの選択が変わったら、Save As欄に選択中の名前を反映する（更新時の再入力を不要にする）。
        [UIAction("select-preset")]
        private void SelectPreset(string value)
        {
            PresetName = value == NoPreset ? "" : value;
            NotifyPropertyChanged(nameof(PresetName));
        }

        // 選択中のプリセットを読み込み、全項目を画面へ反映する。
        [UIAction("load-preset")]
        private void LoadPreset()
        {
            if (PresetSelected == NoPreset) return;
            if (PresetManager.Load(PresetSelected, _config))
            {
                _config.Changed();
                NotifyAllUi();
            }
        }

        // 現在の設定をプリセットとして保存する（名前未入力なら選択中へ上書き）。
        [UIAction("save-preset")]
        private void SavePreset()
        {
            var name = string.IsNullOrWhiteSpace(PresetName) ? PresetSelected : PresetName.Trim();
            if (string.IsNullOrWhiteSpace(name) || name == NoPreset) return;
            // 保存に使った名前（ファイル名に使えない文字を除いたもの）で、選択と Save As 欄を合わせる
            var saved = PresetManager.Save(name, _config);
            if (saved != null)
            {
                RebuildPresetOptions(saved);
                PresetName = saved;
                NotifyPropertyChanged(nameof(PresetName));
                NotifyPropertyChanged(nameof(PresetOptions));
                NotifyPropertyChanged(nameof(PresetSelected));
            }
        }

        // 選択中のプリセットを削除する。
        [UIAction("delete-preset")]
        private void DeletePreset()
        {
            if (PresetSelected == NoPreset) return;
            PresetManager.Delete(PresetSelected);
            RebuildPresetOptions(null);
            PresetName = "";  // 消した名前が Save As 欄に残ると、そのまま Save して作り直してしまう
            NotifyPropertyChanged(nameof(PresetName));
            NotifyPropertyChanged(nameof(PresetOptions));
            NotifyPropertyChanged(nameof(PresetSelected));
        }

        // ───── Hide Objects per Environment ───────────────────────
        // 環境ごとの非表示リスト（UserData/SkyLight/Environments/HideObjects.json）を画面から編集する。
        // ドロップダウンの表示は環境の表示名（例 Interscope）、候補は非表示中なら先頭に HiddenMark を付ける。
        private const string NoEnv = "—";
        private const string HiddenMark = "[Hidden] ";
        private readonly List<string> _envKeys = new List<string>(); // EnvOptions と同じ順の serializedName

        [UIComponent("env-dropdown")]
        private DropDownListSetting _envDropdown = null!;

        [UIComponent("env-object-dropdown")]
        private DropDownListSetting _envObjectDropdown = null!;

        [UIValue("env-options")]
        public List<object> EnvOptions { get; private set; } = new List<object> { NoEnv };

        [UIValue("env-selected")]
        public string EnvSelected { get; set; } = NoEnv;

        [UIValue("env-object-options")]
        public List<object> EnvObjectOptions { get; private set; } = new List<object> { NoEnv };

        [UIValue("env-object-selected")]
        public string EnvObjectSelected { get; set; } = NoEnv;

        // 選んでいる環境の Hide（; 区切り）。直接書き換えると保存する。
        [UIValue("env-hide")]
        public string EnvHide
        {
            get => SelectedEnvKey is string key ? EnvironmentHideList.GetHideHints(key) : "";
            set
            {
                if (!(SelectedEnvKey is string key)) return;
                EnvironmentHideList.SetHideHints(key, NormalizeHints(value));
                RebuildEnvObjectOptions();
            }
        }

        private string? SelectedEnvKey
        {
            get
            {
                int i = EnvOptions.IndexOf(EnvSelected);
                return i >= 0 && i < _envKeys.Count ? _envKeys[i] : null;
            }
        }

        // 環境の一覧を作り直す。selectKey があればそれを選ぶ（無ければ今の選択を保つ）。
        private void RebuildEnvOptions(string? selectKey)
        {
            var envs = EnvironmentHideList.ListEnvironments();
            _envKeys.Clear();
            var options = new List<object>();
            foreach (var (key, name) in envs)
            {
                _envKeys.Add(key);
                options.Add(string.IsNullOrEmpty(name) || options.Contains(name) ? key : name); // 表示名（重なったら serializedName）
            }
            if (options.Count == 0) options.Add(NoEnv);
            EnvOptions = options;

            int idx = string.IsNullOrEmpty(selectKey) ? -1 : _envKeys.IndexOf(selectKey!);
            if (idx >= 0) EnvSelected = (string)EnvOptions[idx];
            else if (!EnvOptions.Contains(EnvSelected)) EnvSelected = (string)EnvOptions[0];

            if (_envDropdown != null)
            {
                _envDropdown.Values = EnvOptions;
                _envDropdown.Value = EnvSelected;
                _envDropdown.UpdateChoices();
                _envDropdown.ReceiveValue();
            }
            RebuildEnvObjectOptions();
        }

        // 選んでいる環境の候補を作り直す（非表示中のものに印を付ける）。
        private void RebuildEnvObjectOptions()
        {
            var key = SelectedEnvKey;
            var hidden = SplitHints(key != null ? EnvironmentHideList.GetHideHints(key) : "");
            var plainSelected = StripMark(EnvObjectSelected);

            var hiddenBy = key != null ? EnvironmentHideList.GetHiddenBy(key) : new Dictionary<string, string>();

            var options = new List<object>();
            if (key != null)
                foreach (var c in EnvironmentHideList.GetCandidates(key))
                    options.Add(MarkFor(c, hidden, hiddenBy) + c);
            if (options.Count == 0) options.Add(NoEnv);
            EnvObjectOptions = options;

            EnvObjectSelected = options.Cast<string>().FirstOrDefault(o => StripMark(o) == plainSelected) ?? (string)options[0];
            if (_envObjectDropdown != null)
            {
                _envObjectDropdown.Values = EnvObjectOptions;
                _envObjectDropdown.Value = EnvObjectSelected;
                _envObjectDropdown.UpdateChoices();
                _envObjectDropdown.ReceiveValue();
            }
            NotifyPropertyChanged(nameof(EnvHide));
        }

        [UIAction("select-env")]
        private void SelectEnv(string value)
        {
            EnvSelected = value;
            RebuildEnvObjectOptions();
        }

        [UIAction("select-env-object")]
        private void SelectEnvObject(string value) => EnvObjectSelected = value;

        [UIAction("env-last-played")]
        private void SelectLastPlayed() => RebuildEnvOptions(EnvironmentHideList.LastPlayed);

        [UIAction("env-hide-object")]
        private void HideSelectedObject() => ChangeSelectedObject(hide: true);

        [UIAction("env-show-object")]
        private void ShowSelectedObject() => ChangeSelectedObject(hide: false);

        private void ChangeSelectedObject(bool hide)
        {
            var key = SelectedEnvKey;
            var name = StripMark(EnvObjectSelected);
            if (key == null || name == NoEnv || name.Length == 0) return;

            var list = SplitHints(EnvironmentHideList.GetHideHints(key));
            list.RemoveAll(h => string.Equals(h, name, System.StringComparison.OrdinalIgnoreCase));
            if (hide) list.Add(name);
            EnvironmentHideList.SetHideHints(key, string.Join(";", list));
            RebuildEnvObjectOptions();
        }

        // 候補に付ける印。
        //   [Hidden]        … この環境の Hide に名前そのものが入っている（Show で戻せる）
        //   [Hidden:Car]    … Hide の別の名前（部分一致）で隠れている
        //   [Main]          … 前回遊んだとき、SkyLight の設定（MAIN の Ring など）で消えていた
        //   [Part]          … 前回遊んだとき、一部だけ消えていた（まとまりの名前など）
        //   印なし          … 見えている
        // どの設定で消えたか（Ring など）は HideObjects.json の HiddenBy にある。一覧に出すと長すぎて折り返し、
        // 下の Show ボタンに重なったので出さない。
        // Main/Part は遊んだ時点の状態なので、MAIN を切り替えたあとはその環境をもう一度遊ぶまで変わらない。
        private static string MarkFor(string name, List<string> hidden, Dictionary<string, string> hiddenBy)
        {
            if (hidden.Contains(name, System.StringComparer.OrdinalIgnoreCase)) return HiddenMark;
            var partial = hidden.FirstOrDefault(h => name.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (partial != null) return $"[Hidden:{partial}] ";
            if (hiddenBy.TryGetValue(name, out var by) && by.Length > 0)
                return by.StartsWith("Part:") ? "[Part] " : "[Main] ";
            return "";
        }

        // 先頭の印（"[...] "）を外す。
        private static string StripMark(string s)
            => System.Text.RegularExpressions.Regex.Replace(s ?? "", @"^\[[^\]]*\] ", "");

        private static List<string> SplitHints(string s)
            => (s ?? "").Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

        private static string NormalizeHints(string s) => string.Join(";", SplitHints(s));
    }
}
