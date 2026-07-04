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
    [ViewDefinition("SkyLight.Views.SkyLightSettingsView.bsml")]
    [HotReload(RelativePathToLayout = @"..\Views\SkyLightSettingsView.bsml")]
    public class SkyLightSettingsController : BSMLAutomaticViewController
    {
        private PluginConfig _config = null!;

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
            set { _config.RecolorBackground = value; _config.Changed(); }
        }

        [UIValue("allow-bloom-with-background")]
        public bool AllowBloomWithBackground
        {
            get => _config.AllowBloomWithBackground;
            set { _config.AllowBloomWithBackground = value; _config.Changed(); }
        }

        [UIValue("background-color")]
        public Color BackgroundColor
        {
            get => ColorUtil.ParseHex(_config.BackgroundColor, new Color(0.29f, 0.48f, 0.71f));
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
            set { _config.ShowFloor = value; _config.Changed(); }
        }

        [UIValue("show-side-lanes")]
        public bool ShowSideLanes
        {
            get => _config.ShowSideLanes;
            set { _config.ShowSideLanes = value; _config.Changed(); }
        }

        [UIValue("floor-color")]
        public Color FloorColor
        {
            get => ColorUtil.ParseHex(_config.FloorColor, new Color(0.13f, 0.16f, 0.19f));
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

        [UIValue("floor-direct-paint")]
        public bool FloorDirectPaint
        {
            get => _config.FloorDirectPaint;
            set { _config.FloorDirectPaint = value; _config.Changed(); }
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
            set { _config.ShowStructures = value; _config.Changed(); }
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
            get => ColorUtil.ParseHex(_config.StructureColor, new Color(0.25f, 0.38f, 0.63f));
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
            set { _config.ShowRing = value; _config.Changed(); }
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
            get => ColorUtil.ParseHex(_config.RingColor, new Color(0.227f, 0.627f, 1f));
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
            // PluginConfig の初期化子を単一の正として使い、既定値インスタンスから読み戻す。
            var d = new PluginConfig();

            _config.Enabled = d.Enabled;
            _config.RecolorBackground = d.RecolorBackground;
            _config.BackgroundColor = d.BackgroundColor;
            _config.BackgroundBrightness = d.BackgroundBrightness;
            _config.Bloom = d.Bloom;
            _config.HideNeon = d.HideNeon;
            _config.ShowFloor = d.ShowFloor;
            _config.ShowSideLanes = d.ShowSideLanes;
            _config.PaintFloor = d.PaintFloor;
            _config.FloorColor = d.FloorColor;
            _config.FloorBrightness = d.FloorBrightness;
            _config.FloorAlpha = d.FloorAlpha;
            _config.FloorDirectPaint = d.FloorDirectPaint;
            _config.ShowStructures = d.ShowStructures;
            _config.PaintStructures = d.PaintStructures;
            _config.StructureColorize = d.StructureColorize;
            _config.StructureColor = d.StructureColor;
            _config.StructureBrightness = d.StructureBrightness;
            _config.StructureAlpha = d.StructureAlpha;
            _config.ShowBars = d.ShowBars;
            _config.BarShaderHints = d.BarShaderHints;
            _config.ShowRing = d.ShowRing;
            _config.PaintRing = d.PaintRing;
            _config.RingShaderHints = d.RingShaderHints;
            _config.RingColorize = d.RingColorize;
            _config.RingColor = d.RingColor;
            _config.RingBrightness = d.RingBrightness;
            _config.RingAlpha = d.RingAlpha;
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
            NotifyPropertyChanged(nameof(Bloom));
            NotifyPropertyChanged(nameof(ShowNeon));
            NotifyPropertyChanged(nameof(ShowFloor));
            NotifyPropertyChanged(nameof(ShowSideLanes));
            NotifyPropertyChanged(nameof(PaintFloor));
            NotifyPropertyChanged(nameof(FloorColor));
            NotifyPropertyChanged(nameof(FloorBrightness));
            NotifyPropertyChanged(nameof(FloorAlpha));
            NotifyPropertyChanged(nameof(FloorDirectPaint));
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
        [UIAction("#post-parse")]
        private void PostParse() => RebuildPresetOptions(PresetSelected);

        // 保存済みファイルから選択肢を作り直し、ドロップダウンへ反映する。
        private void RebuildPresetOptions(string? select)
        {
            var names = PresetManager.List();
            PresetOptions = names.Count > 0
                ? names.Cast<object>().ToList()
                : new List<object> { NoPreset };

            if (!string.IsNullOrEmpty(select) && names.Contains(select!))
                PresetSelected = select!;
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
            if (PresetManager.Save(name, _config))
            {
                PresetName = "";
                NotifyPropertyChanged(nameof(PresetName));
                RebuildPresetOptions(name);
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
            NotifyPropertyChanged(nameof(PresetOptions));
            NotifyPropertyChanged(nameof(PresetSelected));
        }
    }
}
