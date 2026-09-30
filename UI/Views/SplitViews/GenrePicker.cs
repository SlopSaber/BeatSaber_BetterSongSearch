using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Components.Settings;
using BetterSongSearch.Configuration;
using BetterSongSearch.Util;
using HMUI;
using IPA.Utilities;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterSongSearch.UI.SplitViews {
	class GenrePicker {
		public static readonly GenrePicker instance = new GenrePicker();
		GenrePicker() { }

		class FilterPresetRow {
			public enum State {
				None,
				Include,
				Exclude
			}

			public readonly ulong value;
			public readonly string name;
			public readonly string mappedName;
			public State selectionState { get; private set; } = State.None;
			//[UIComponent("label")] readonly TextMeshProUGUI label = null;
			[UIComponent("excludeButton")] readonly ClickableText excludeButton = null;
			[UIComponent("includeButton")] readonly ClickableText includeButton = null;

			public FilterPresetRow(KeyValuePair<string, ulong> tag, int count, ulong included, ulong excluded) {
				this.value = tag.Value;
				this.name = tag.Key;

				this.mappedName = FilterOptions.FormatBeatSaverTag(this.name);

				this.mappedName += $" ({count})";

				this.selectionState =
					(included & this.value) != 0 ? State.Include :
					(excluded & this.value) != 0 ? State.Exclude :
					State.None;
			}

			public void SetSelectionState(State newState) {
				this.selectionState = newState;
				Refresh(true, true);
			}

			[UIAction("ExcludeGenre")]
			public void ExcludeGenre() {
				SetSelectionState(State.Exclude);
			}

			[UIAction("IncludeGenre")]
			public void IncludeGenre() {
				SetSelectionState(selectionState == State.Include ? State.None : State.Include);
			}

			[UIAction("refresh-visuals")]
			public void Refresh(bool selected, bool highlighted) {
				includeButton.fontStyle = 
					selectionState == State.Exclude ? 
					FontStyles.Strikethrough : 
					FontStyles.Normal;

				includeButton.color = new UnityEngine.Color(
					selectionState == State.Exclude ? 1f : .8f,
					selectionState == State.Include ? 1f : .7f,
					.8f,
					highlighted ? 1f : 0.8f
				);

				excludeButton.gameObject.SetActive(highlighted && selectionState != State.Exclude);
			}
		}

		[UIAction("#post-parse")]
		void Parsed() {
			SongDetailsCache.SongDetailsContainer.dataAvailableOrUpdated += Reload;
		}


		[UIComponent("genreList")] readonly CustomCellListTableData genreList = null;
		int reloadRevision;
		internal async void Reload() {
			var revision = ++reloadRevision;
			var included = FilterView.currentFilter._mapGenreBitfield;
			var excluded = FilterView.currentFilter._mapGenreExcludeBitfield;
			var token = BSSFlowCoordinator.closeCancelSource.Token;
			try {
				List<object> rows;
				int dataset;
				await BSSFlowCoordinator.dataProcessingSlot.WaitAsync(token);
				try {
					dataset = BSSFlowCoordinator.DatasetRevision;
					var details = BSSFlowCoordinator.songDetails;
					var songs = details.songs;
					var tags = details.tags.ToArray();
					rows = await Task.Run(() => {
						var tagCounts = new Dictionary<ulong, int>();
						for(var i = 0; i < songs.Length; i++) {
							if((i & 255) == 0) token.ThrowIfCancellationRequested();
							for(var bits = songs[i].tags; bits != 0; bits &= bits - 1) {
								var bit = bits & (~bits + 1);
								tagCounts.TryGetValue(bit, out var count);
								tagCounts[bit] = count + 1;
							}
						}
						return tags
							.Where(x => !FilterOptions.mapStyles.Contains(x.Key))
							.OrderBy(x => x.Key)
							.Select(x => {
								if(!tagCounts.TryGetValue(x.Value, out var count) && (x.Value & (x.Value - 1)) != 0) {
									for(var i = 0; i < songs.Length; i++)
										if((songs[i].tags & x.Value) != 0)
											count++;
								}
								return new FilterPresetRow(x, count, included, excluded);
							}).ToList<object>();
					}, token);
				} finally {
					BSSFlowCoordinator.dataProcessingSlot.Release();
				}
				await UnityGame.SwitchToMainThreadAsync();
				if(token.IsCancellationRequested || revision != reloadRevision || BSSFlowCoordinator.isClosing) return;
				if(dataset != BSSFlowCoordinator.DatasetRevision) {
					Reload();
					return;
				}
				genreList.Data = rows;
				genreList.TableView.ReloadData();
				genreList.TableView.ClearSelection();
			} catch(OperationCanceledException) when (token.IsCancellationRequested) {
			} catch(Exception ex) {
				Plugin.Log.Error($"Failed to prepare genre list: {ex}");
			}
		}

		void GenreSelected(object _, FilterPresetRow row) { }

		void SelectGenre() {
			var selectedGenres = new List<string>();
			var excludedGenres = new List<string>();
			foreach(var _entry in genreList.Data) {
				var fpr = (FilterPresetRow)_entry;

				if(fpr.selectionState == FilterPresetRow.State.Include)
					selectedGenres.Add(fpr.name);
				else if(fpr.selectionState == FilterPresetRow.State.Exclude)
					excludedGenres.Add(fpr.name);
			}

			BSSFlowCoordinator.filterView.SetGenreFilter(selectedGenres, excludedGenres);
		}

		void ClearGenre() {
			BSSFlowCoordinator.filterView.SetGenreFilter(null, null);
		}
	}
}
