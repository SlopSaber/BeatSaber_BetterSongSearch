using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.Parser;
using BeatSaberPlaylistsLib;
using BeatSaberPlaylistsLib.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IPA.Utilities;
using BetterSongSearch.Configuration;
using TMPro;

namespace BetterSongSearch.UI.SplitViews {
	class PlaylistCreation {
		public static readonly PlaylistCreation instance = new PlaylistCreation();
		PlaylistCreation() { }

		[UIComponent("playlistSongsCountSlider")] readonly SliderSetting playlistSongsCountSlider = null;
		[UIComponent("playlistName")] readonly StringSetting playlistName = null;
		[UIComponent("resultText")] readonly TextMeshProUGUI resultText = null;

		[UIParams] readonly BSMLParserParams parserParams = null;

		[UIAction("#post-parse")]
		void Parsed() {
			playlistName.ModalKeyboard.ModalView._animateParentCanvas = false;
		}

		internal static string nameToUseOnNextOpen = "h";
		static bool clearExisting = true;
		static bool hightlightDiffs = false;

		public void Open() {
			if(IPA.Loader.PluginManager.GetPluginFromId("BeatSaberPlaylistsLib") == null) {
				resultText.text = "You dont have 'BeatSaberPlaylistsLib' installed which is required to create Playlists. You can get it in ModAssistant.";
				parserParams.EmitEvent("ShowResultModal");
				return;
			}

			if(nameToUseOnNextOpen != null) {
				playlistName.Text = nameToUseOnNextOpen;
				nameToUseOnNextOpen = null;
			}

			parserParams.EmitEvent("ShowModal");
		}

		void ShowResult(string text) {
			resultText.text = text;
			parserParams.EmitEvent("CloseModal");
			parserParams.EmitEvent("ShowResultModal");
		}

		static readonly IReadOnlyDictionary<SongDetailsCache.Structs.MapCharacteristic, string> songDetailsCharNames
			= Enum.GetValues(typeof(SongDetailsCache.Structs.MapCharacteristic))
			.Cast<SongDetailsCache.Structs.MapCharacteristic>()
			.ToDictionary(x => x, x => x.ToString());

		static readonly IReadOnlyDictionary<SongDetailsCache.Structs.MapDifficulty, string> songDetailsDiffNames
			= Enum.GetValues(typeof(SongDetailsCache.Structs.MapDifficulty))
			.Cast<SongDetailsCache.Structs.MapDifficulty>()
			.ToDictionary(x => x, x => x.ToString());

		bool creating;
		async void CreatePlaylist() {
			if(creating) return;
			var fName = string.Concat(playlistName.Text.Split(Path.GetInvalidFileNameChars())).Trim();

			if(fName.Length == 0) {
				ShowResult("Your Playlist name is invalid");
				return;
			}

			creating = true;
			var title = playlistName.Text;
			var clear = clearExisting;
			var highlight = hightlightDiffs;
			var limit = playlistSongsCountSlider.Value;
			var filter = FilterView.currentFilter.Clone();
			var searchTerm = BSSFlowCoordinator.songListView.songSearchInput.text;
			var sortMode = SongListController.selectedSortMode;
			try {
				PreparedSong[] songs;
				string serializedFilter;
				await BSSFlowCoordinator.dataProcessingSlot.WaitAsync();
				try {
					var results = SongListController.searchedSongsList;
					var prepared = await Task.Run(() => {
						var entries = results.Select(s => new PreparedSong {
							Hash = s.hash, Name = s.detailsSong.songName, Key = s.detailsSong.key,
							Author = s.detailsSong.levelAuthorName,
							Difficulties = highlight ? s.diffs.Where(x => x.passesFilter)
								.Select(x => (songDetailsCharNames[x.detailsDiff.characteristic],
									songDetailsDiffNames[x.detailsDiff.difficulty])).ToArray()
								: Array.Empty<(string, string)>()
						}).ToArray();
						return (entries, filter.Serialize(Newtonsoft.Json.Formatting.None));
					});
					songs = prepared.Item1;
					serializedFilter = prepared.Item2;
				} finally {
					BSSFlowCoordinator.dataProcessingSlot.Release();
				}
				await UnityGame.SwitchToMainThreadAsync();
				if(BSSFlowCoordinator.isClosing) return;
				var manager = PlaylistManager.DefaultManager.CreateChildManager("BetterSongSearch");

				if(!manager.TryGetPlaylist(fName, out var plist))
					plist = manager.CreatePlaylist(
						fName,
						title,
						"BetterSongSearch",
						""
					);

				if(clear)
					plist.Clear();

				plist.SetCustomData("BetterSongSearchFilter", serializedFilter);
				plist.SetCustomData("BetterSongSearchSearchTerm", searchTerm);
				plist.SetCustomData("BetterSongSearchSort", sortMode);
				// PlaylistLib duplicate check is O(n^2) - Not gud enough for batch-adding with thousands of entries, so we roll out own
				plist.AllowDuplicates = true;

				// PlaylistLib contains uppercase hashes, but in that case I dont have control over it and dont know if it might change
				var songsAlreadyInPlaylist = plist.Select(x => x.Hash.ToUpperInvariant()).ToHashSet();

				int addedSongs = 0;

				for(var i = 0; i < songs.Length; i++) {
					if(addedSongs >= limit)
						break;

					var s = songs[i];

					PlaylistSong pls = null;
					// SongDetails returns uppercase hashes
					var uH = s.Hash;

					if(!songsAlreadyInPlaylist.Contains(uH))
						pls = (PlaylistSong)plist.Add(uH, s.Name, s.Key, s.Author);

					if(pls == null)
						continue;

					songsAlreadyInPlaylist.Add(uH);

					addedSongs++;

					if(!highlight)
						continue;

					foreach(var x in s.Difficulties) {
						pls.AddDifficulty(x.Item1, x.Item2);
					}
				}

				plist.AllowDuplicates = false;
				manager.StorePlaylist(plist);
				manager.RequestRefresh("BetterSongSearch");

				ShowResult($"Added <b><color=#CCC>{addedSongs}</color></b> Songs to Playlist <b><color=#CCC>{title}</color></b> (Contains {plist.Count} now)");
			} catch(Exception ex) {
				ShowResult($"Playlist failed to Create: More details in log, {ex.GetType().Name}");
				Plugin.Log.Warn("Failed to create Playlist:");
				Plugin.Log.Warn(ex);
			} finally {
				creating = false;
			}
		}

		sealed class PreparedSong {
			public string Hash, Name, Key, Author;
			public (string, string)[] Difficulties;
		}
	}
}
