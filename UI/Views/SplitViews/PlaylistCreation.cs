using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.Parser;
using BeatSaberPlaylistsLib;
using BeatSaberPlaylistsLib.Types;
using BeatSaberPlaylistsLib.Legacy;
using BeatSaberPlaylistsLib.Blist;
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
		Task pendingSave;
		internal void Flush() => pendingSave?.GetAwaiter().GetResult();
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
			var token = BSSFlowCoordinator.closeCancelSource.Token;
			try {
				PreparedSong[] songs;
				string serializedFilter;
				await BSSFlowCoordinator.dataProcessingSlot.WaitAsync(token);
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
					}, token);
					songs = prepared.Item1;
					serializedFilter = prepared.Item2;
				} finally {
					BSSFlowCoordinator.dataProcessingSlot.Release();
				}
				await UnityGame.SwitchToMainThreadAsync();
				if(token.IsCancellationRequested || BSSFlowCoordinator.isClosing) return;
				var root = await Task.Run(() => PlaylistManager.DefaultManager, token);
				await UnityGame.SwitchToMainThreadAsync();
				var manager = await root.CreateChildManagerAsync("BetterSongSearch");
				await UnityGame.SwitchToMainThreadAsync();
				if(token.IsCancellationRequested || BSSFlowCoordinator.isClosing) return;

				if(!manager.TryGetPlaylist(fName, out var plist))
					plist = manager.CreatePlaylist(fName, title, "BetterSongSearch", "");
				var handler = plist.SuggestedExtension != null ? manager.GetHandlerForExtension(plist.SuggestedExtension) : null;
				handler ??= manager.GetHandlerForPlaylistType(plist.GetType());
				int addedSongs;
				IPlaylist draft = null;
				Func<Action> preparePublication = null;
				if(plist.GetType() == typeof(LegacyPlaylist) && handler?.GetType() == typeof(LegacyPlaylistHandler)
					&& (clear || plist.All(song => song.GetType() == typeof(LegacyPlaylistSong)))) {
					var snapshot = ((LegacyPlaylist)plist).CaptureSnapshot(!clear);
					draft = snapshot.Playlist;
					preparePublication = snapshot.PrepareSongPublication;
				} else if(plist.GetType() == typeof(BlistPlaylist) && handler?.GetType() == typeof(BlistPlaylistHandler)
					&& (clear || plist.All(song => song.GetType() == typeof(BlistPlaylistSong)))) {
					var snapshot = ((BlistPlaylist)plist).CaptureSnapshot(!clear);
					draft = snapshot.Playlist;
					preparePublication = snapshot.PrepareSongPublication;
				}
				if(draft != null) {
					var extension = handler.SupportsExtension(plist.SuggestedExtension) ? plist.SuggestedExtension : handler.DefaultExtension;
					var fileName = plist.Filename;
					var save = manager.QueuePlaylistFileOperation(plist, directory => {
						token.ThrowIfCancellationRequested();
						var count = PopulatePlaylist(draft, songs, limit, highlight, serializedFilter, searchTerm, sortMode);
						token.ThrowIfCancellationRequested();
						SavePlaylist(handler, draft, Path.Combine(directory, fileName + "." + extension));
						return (Count: count, Publish: preparePublication());
					});
					pendingSave = save;
					var result = await save;
					await UnityGame.SwitchToMainThreadAsync();
					pendingSave = null;
					await manager.WaitForPlaylistFilePublicationAsync(plist);
					plist.SetCustomData("BetterSongSearchFilter", serializedFilter);
					plist.SetCustomData("BetterSongSearchSearchTerm", searchTerm);
					plist.SetCustomData("BetterSongSearchSort", sortMode);
					plist.AllowDuplicates = false;
					result.Publish();
					manager.CompletePlaylistSave(plist);
					addedSongs = result.Count;
				} else {
					await manager.WaitForPlaylistFilePublicationAsync(plist);
					if(clear) plist.Clear();
					addedSongs = PopulatePlaylist(plist, songs, limit, highlight, serializedFilter, searchTerm, sortMode);
					manager.StorePlaylist(plist);
				}
				manager.RequestRefresh("BetterSongSearch");
				if(!token.IsCancellationRequested && !BSSFlowCoordinator.isClosing)
					ShowResult($"Added <b><color=#CCC>{addedSongs}</color></b> Songs to Playlist <b><color=#CCC>{title}</color></b> (Contains {plist.Count} now)");
			} catch(OperationCanceledException) {
			} catch(Exception ex) {
				await UnityGame.SwitchToMainThreadAsync();
				if(!token.IsCancellationRequested && !BSSFlowCoordinator.isClosing)
					ShowResult($"Playlist failed to Create: More details in log, {ex.GetType().Name}");
				Plugin.Log.Warn("Failed to create Playlist:");
				Plugin.Log.Warn(ex);
			} finally {
				creating = false;
			}
		}

		static void SavePlaylist(IPlaylistHandler handler, IPlaylist playlist, string destination) {
			var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try {
				handler.SerializeToFile(playlist, temporary);
				if(File.Exists(destination)) File.Replace(temporary, destination, null);
				else File.Move(temporary, destination);
			} finally {
				if(File.Exists(temporary)) File.Delete(temporary);
			}
		}

		static int PopulatePlaylist(IPlaylist playlist, PreparedSong[] songs, float limit, bool highlight,
			string serializedFilter, string searchTerm, string sortMode) {
			playlist.SetCustomData("BetterSongSearchFilter", serializedFilter);
			playlist.SetCustomData("BetterSongSearchSearchTerm", searchTerm);
			playlist.SetCustomData("BetterSongSearchSort", sortMode);
			playlist.AllowDuplicates = true;
			var existing = new HashSet<string>(playlist.Where(x => x.Hash != null).Select(x => x.Hash), StringComparer.OrdinalIgnoreCase);
			int added = 0;
			foreach(var song in songs) {
				if(added >= limit) break;
				if(existing.Contains(song.Hash)) continue;
				var entry = (PlaylistSong)playlist.Add(song.Hash, song.Name, song.Key, song.Author);
				if(entry == null) continue;
				existing.Add(song.Hash);
				added++;
				if(highlight)
					foreach(var diff in song.Difficulties) entry.AddDifficulty(diff.Item1, diff.Item2);
			}
			playlist.AllowDuplicates = false;
			return added;
		}

		sealed class PreparedSong {
			public string Hash, Name, Key, Author;
			public (string, string)[] Difficulties;
		}
	}
}
