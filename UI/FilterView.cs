using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.Parser;
using BeatSaberMarkupLanguage.ViewControllers;
using BetterSongSearch.Configuration;
using BetterSongSearch.Util;
using HarmonyLib;
using HMUI;
using SongDetailsCache.Structs;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace BetterSongSearch.UI {

	[HotReload(RelativePathToLayout = @"Views\FilterView.bsml")]
	[ViewDefinition("BetterSongSearch.UI.Views.FilterView.bsml")]
	class FilterView : BSMLAutomaticViewController, INotifyPropertyChanged {
		public static List<DateTime> hideOlderThanOptions { get; private set; } = BuildList();

		static List<DateTime> BuildList() {
			var hideOlderThanOptions = new List<DateTime>();

			for(var x = new DateTime(2018, 5, 1); x < DateTime.Now; x = x.AddMonths(1))
				hideOlderThanOptions.Add(x);

			return hideOlderThanOptions;
		}

		public static readonly FilterOptions currentFilter = new FilterOptions();

		[UIComponent("filterbarContainer")] Transform filterbarContainer = null;
		//[UIComponent("modsRequirementDropdown")] DropdownWithTableView _modsRequirementDropdown = null;

		IEnumerator FixupScrollpanel() {
			yield return null;

			var beatsaverFilterScroller = gameObject.GetComponentInChildren<BSMLScrollView>().transform;

			((RectTransform)beatsaverFilterScroller.Find("Viewport").transform).sizeDelta = new Vector2(-5.5f, -5);

			foreach(var input in
				beatsaverFilterScroller.Find("Viewport/BSMLScrollViewContent/BSMLScrollViewContentContainer/BSMLVerticalLayoutGroup")
				.GetComponentsInChildren<Touchable>()
			) {
				RectTransform t = (RectTransform)input.transform;

				if(input.name == "IncButton" || input.name == "DropDownButton") {
					t = (RectTransform)t.parent;
					t.Find("DecButton")?.gameObject.SetActive(false);
				}

				if(t.sizeDelta.x > 30)
					t.sizeDelta = new Vector2(30, 0);
			}
		}

		[UIAction("#post-parse")]
		void Parsed() {
			currentFilter.hideOlderThanSlider.Slider.maxValue = hideOlderThanOptions.Count - 1;

			((RectTransform)gameObject.transform).offsetMax = new Vector2(20, 22);

			StartCoroutine(BSMLStuff.MergeSliders(gameObject));
			StartCoroutine(FixupScrollpanel());

			//// I hate BSML some times
			var m = GetComponentsInChildren<DropDownListSetting>()
				.Where(x => x.AssociatedValue.MemberName == "mods")
				.First()
				.GetComponent<DropdownWithTableView>()
				._modalView;

			foreach(var l in GetComponentsInChildren<CurvedTextMeshPro>().Where(x => x.gameObject.name == "Title"))
				l.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
				

			((RectTransform)m.transform).pivot = new Vector2(0.5f, 0.3f);

			// This is garbage
			foreach(var x in GetComponentsInChildren<Backgroundable>().Select(x => x.GetComponent<ImageView>())) {
				if(!x || x.color0 != Color.white || x.sprite.name != "RoundRect10")
					continue;

				x._skew = 0;
				x.overrideSprite = null;
				x.SetImageAsync("#RoundRect10BorderFade");
				x.color = new Color(0, 0.7f, 1f, 0.4f);
			}

			foreach(var x in filterbarContainer.GetComponentsInChildren<ImageView>().Where(x => x.gameObject.name == "Underline"))
				x.SetImageAsync("#RoundRect10BorderFade");
		}

		internal void ClearFilters() => SetFilter();

		internal void SetFilter(FilterOptions filter = null) {
			filter ??= new FilterOptions();
			foreach(var x in AccessTools.GetDeclaredProperties(typeof(FilterOptions))) {
				if(!x.CanWrite || x.Name[0] == '_')
					continue;

				x.SetValue(currentFilter, x.GetValue(filter));
			}

			SetGenreFilter(null, null);
			currentFilter.NotifyPropertiesChanged();
			/*
			 * This is a massive hack and I have NO IDEA why I need to do this. If I dont do this, or
			 * the FilterSongs() method NEVER ends up being called, not even if I manually invoke
			 * limitedUpdateData.Call[NextFrame]() OR EVEN BSSFlowCoordinator.FilterSongs() DIRECTLY
			 * Seems like there is SOMETHING broken with how input changes are handled, something to do
			 * with nested coroutines or whatever. I have no idea. For now I spent enough time trying to fix this
			 */
			currentFilter.hideOlderThanSlider.OnChange.Invoke(currentFilter.hideOlderThanSlider.Value);
		}

		BSMLParserParams presetsViewParams = null;
		[UIAction("ShowPresets")] void ShowPresets() {
			BSMLStuff.InitSplitView(ref presetsViewParams, gameObject, SplitViews.Presets.instance).EmitEvent("OpenPresets");

			SplitViews.Presets.instance.ReloadPresets();
		}


		BSMLParserParams genreViewParams = null;
		
		internal void ShowGenrePicker() {
			BSMLStuff.InitSplitView(ref genreViewParams, gameObject, SplitViews.GenrePicker.instance).EmitEvent("OpenGenreModal");

			SplitViews.GenrePicker.instance.Reload();
		}

		internal void SetGenreFilter(List<string> includedGenres, List<string> excludedGenres) {
			currentFilter.mapGenreString = includedGenres == null ? "" : string.Join(",", includedGenres);
			currentFilter.mapGenreExcludeString = excludedGenres == null ? "" : string.Join(",", excludedGenres);

			var genrefilter = "Any";

			if(includedGenres?.Count > 0 || excludedGenres?.Count > 0)
				genrefilter = $"{includedGenres?.Count ?? 0} Incl., {excludedGenres?.Count ?? 0} Excl.";

			currentFilter.genrePickButton.GetComponentInChildren<CurvedTextMeshPro>().text = genrefilter;

			FilterOptions.UpdateData();
		}


		#region filters
		static bool RequiresScore(FilterOptions filter, string sortMode) => filter.existingScore == (string)FilterOptions.scoreFilterOptions[2] || sortMode == "Worst local score";

		static readonly IReadOnlyDictionary<object, MapMods> funnyMapThing = Enumerable.Range(0, 5)
			.ToDictionary(x => FilterOptions.modOptions[x + 1], x => (MapMods)(1 << x));

		static readonly IReadOnlyDictionary<string, RankedStates> funnyMapThing2 = Enumerable.Range(0, 4)
			.ToDictionary(x => (string)FilterOptions.rankedFilterOptions[x + 1], x => (RankedStates)(1 << x));

		public bool DifficultyCheck(in SongDifficulty diff, FilterOptions options = null, string sortMode = null) {
			var filter = options ?? currentFilter;
			if(filter.difficulty_int != -1 && filter.difficulty_int != (int)diff.difficulty)
				return false;

			if(filter.characteristic_int != -1 && filter.characteristic_int != (int)diff.characteristic)
				return false;

			if(diff.njs < filter.minimumNjs || diff.njs > filter.maximumNjs)
				return false;

			if(filter.rankedState != (string)FilterOptions.rankedFilterOptions[0]) {
				var state = funnyMapThing2[filter.rankedState];

				if(state == RankedStates.ScoresaberRanked && diff.stars == 0)
					return false;

				if(state == RankedStates.BeatleaderRanked && diff.starsBeatleader == 0)
					return false;
			}

			if(filter.mods != (string)FilterOptions.modOptions[0]) {
				if((diff.mods & funnyMapThing[filter.mods]) == 0)
					return false;
			}

			if(diff.song.songDurationSeconds > 0) {
				var nps = diff.notes / (float)diff.song.songDurationSeconds;

				if(nps < filter.minimumNps || nps > filter.maximumNps)
					return false;
			}

			return true;
		}

		public bool SearchDifficultyCheck(SongSearchSong.SongSearchDiff diff, FilterOptions options = null, string sortMode = null) {
			var filter = options ?? currentFilter;
			if(filter.existingScore != (string)FilterOptions.scoreFilterOptions[0] || RequiresScore(filter, sortMode ?? SongListController.selectedSortMode)) {
				if(diff.CheckHasScore() != RequiresScore(filter, sortMode ?? SongListController.selectedSortMode))
					return false;
			}

			var star = -1f;

			if(filter.maximumStars != float.MaxValue) {
				star = diff.GetStarsForRankedState(filter.rankedState);

				if(star > filter.maximumStars)
					return false;
			}

			if(filter.minimumStars != 0f) {
				if(star == -1)
					star = diff.GetStarsForRankedState(filter.rankedState);

				if(star < filter.minimumStars)
					return false;
			}

			return true;
		}

		public bool SongCheck(in Song song, FilterOptions options = null, string sortMode = null) {
			var filter = options ?? currentFilter;
			if(song.uploadTime < filter.hideOlderThan)
				return false;

			if(filter.rankedState != (string)FilterOptions.rankedFilterOptions[0]) {
				if(!song.rankedStates.HasFlag(funnyMapThing2[filter.rankedState]))
					return false;
			}

			const float oneSixtythInverse = 1f / 60;

			if(song.songDurationSeconds > 0f) {
				var x = song.songDurationSeconds * oneSixtythInverse;

				if(x < filter.minimumSongLength || x > filter.maximumSongLength)
					return false;
			}

			var voteCount = song.downvotes + song.upvotes;

			if(voteCount < filter.minimumVotes)
				return false;

			if(filter.minimumRating > 0f && (filter.minimumRating > song.rating || voteCount == 0))
				return false;

			if(filter.onlyCuratedMaps && (song.uploadFlags & UploadFlags.Curated) == 0)
				return false;

			if(filter.onlyVerifiedMappers && (song.uploadFlags & UploadFlags.VerifiedUploader) == 0)
				return false;

			if(filter._mapStyleBitfield != 0 && (song.tags & filter._mapStyleBitfield) == 0)
				return false;

			if(filter._mapGenreBitfield != 0 && (song.tags & filter._mapGenreBitfield) == 0)
				return false;

			if((song.tags & filter._mapGenreExcludeBitfield) != 0)
				return false;

			return true;
		}

		public bool SearchSongCheck(SongSearchSong song, FilterOptions options = null, string sortMode = null) {
			var filter = options ?? currentFilter;
			if(filter.existingSongs != (string)FilterOptions.downloadedFilterOptions[0]) {
				if(SongCore.Collections.songWithHashPresent(song.hash) == (filter.existingSongs == (string)FilterOptions.downloadedFilterOptions[2]))
					return false;
			}

			if(filter.existingScore != (string)FilterOptions.scoreFilterOptions[0] || RequiresScore(filter, sortMode ?? SongListController.selectedSortMode)) {
				if(song.CheckHasScore() != RequiresScore(filter, sortMode ?? SongListController.selectedSortMode))
					return false;
			}

			if(filter.uploaders.Count != 0) {
				if(filter.uploaders.Contains(song.uploaderNameLowercase)) {
					if(filter.uploadersBlacklist)
						return false;
				} else if(!filter.uploadersBlacklist) {
					return false;
				}
			}

			return true;
		}
		#endregion


		[UIComponent("sponsorsText")] CurvedTextMeshPro sponsorsText = null;
		void OpenSponsorsLink() => Process.Start("https://github.com/sponsors/kinsi55");
		static Task<string> sponsorTextTask;
		static async Task<string> FetchSponsors() {
			try {
				using(var client = new WebClient())
					return await client.DownloadStringTaskAsync(new Uri("http://kinsi.me/sponsors/bsout.php"));
			} catch {
				return "Failed to load";
			}
		}
		async void OpenSponsorsModal() {
			sponsorsText.text = "Loading...";
			var request = sponsorTextTask ??= FetchSponsors();
			var desc = await request;
			if(desc == "Failed to load" && sponsorTextTask == request)
				sponsorTextTask = null;
			if(this == null || sponsorsText == null)
				return;

			sponsorsText.text = desc;
			// There is almost certainly a better way to update / correctly set the scrollbar size...
			sponsorsText.gameObject.SetActive(false);
			sponsorsText.gameObject.SetActive(true);
		}


		public static readonly RatelimitCoroutine limitedUpdateData = new RatelimitCoroutine(BSSFlowCoordinator.FilterSongs, 0.1f);

		readonly string version = $"BetterSongSearch v{Assembly.GetExecutingAssembly().GetName().Version.ToString(3)} by Kinsi55";
		[UIComponent("datasetInfoLabel")] private TextMeshProUGUI _datasetInfoLabel = null;
		public TextMeshProUGUI datasetInfoLabel => _datasetInfoLabel;
	}
}
