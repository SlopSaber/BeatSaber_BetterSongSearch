using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Components.Settings;
using BetterSongSearch.Configuration;
using BetterSongSearch.Util;
using HMUI;
using IPA.Utilities.Async;
using System;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine.UI;

namespace BetterSongSearch.UI.SplitViews {
	class Presets {
		public static readonly Presets instance = new Presets();
		Presets() { }

		class FilterPresetRow {
			public readonly string name;
			[UIComponent("label")] readonly TextMeshProUGUI label = null;

			public FilterPresetRow(string name) => this.name = name;

			[UIAction("refresh-visuals")]
			public void Refresh(bool selected, bool highlighted) {
				label.color = new UnityEngine.Color(
					selected ? 0 : 255,
					selected ? 128 : 255,
					selected ? 128 : 255,
					highlighted ? 0.9f : 0.6f
				);
			}
		}

		[UIAction("#post-parse")]
		void Parsed() {
			// BSML / HMUI my beloved
			newPresetName.ModalKeyboard.ModalView._animateParentCanvas = false;
		}


		[UIComponent("loadButton")] readonly NoTransitionsButton loadButton = null;
		[UIComponent("deleteButton")] readonly NoTransitionsButton deleteButton = null;
		[UIComponent("presetList")] readonly CustomCellListTableData presetList = null;
		[UIComponent("newPresetName")] readonly StringSetting newPresetName = null;
		int reloadRevision;
		internal async void ReloadPresets() {
			try {
				await ReloadPresetsAsync();
			} catch(Exception ex) {
				Plugin.Log.Error($"Failed to load filter presets: {ex}");
			}
		}

		async Task ReloadPresetsAsync() {
			var revision = ++reloadRevision;
			loadButton.interactable = false;
			deleteButton.interactable = false;
			await FilterPresets.InitAsync();
			var snapshot = FilterPresets.presets;
			var rows = await Task.Run(() => snapshot.Select(x => new FilterPresetRow(x.Key)).ToList<object>());
			await UnityMainThreadTaskScheduler.Factory.StartNew(() => {
				if(revision != reloadRevision || BSSFlowCoordinator.isClosing) return;
				presetList.Data = rows;
				presetList.TableView.ReloadData();
				presetList.TableView.ClearSelection();
				curSelected = null;
				newPresetName.Text = "";
			});
		}

		string curSelected;
		void PresetSelected(object _, FilterPresetRow row) {
			loadButton.interactable = true;
			deleteButton.interactable = true;
			newPresetName.Text = curSelected = row.name;
		}

		async void AddPreset() {
			var name = newPresetName.Text;
			var filter = FilterView.currentFilter.Clone();
			try {
				await FilterPresets.SaveAsync(name, filter);
				await ReloadPresetsAsync();
			} catch(Exception ex) {
				Plugin.Log.Error($"Failed to save filter preset: {ex}");
			}
		}

		void LoadPreset() {
			PlaylistCreation.nameToUseOnNextOpen = curSelected;

			if(curSelected != null && FilterPresets.presets.TryGetValue(curSelected, out var preset))
				BSSFlowCoordinator.filterView.SetFilter(preset.Clone());
		}
		async void DeletePreset() {
			var name = curSelected;
			if(name == null) return;
			loadButton.interactable = false;
			deleteButton.interactable = false;
			try {
				await FilterPresets.DeleteAsync(name);
				await ReloadPresetsAsync();
			} catch(Exception ex) {
				Plugin.Log.Error($"Failed to delete filter preset: {ex}");
			}
		}
	}
}
