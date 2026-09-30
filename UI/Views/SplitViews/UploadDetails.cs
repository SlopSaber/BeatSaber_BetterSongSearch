using BeatSaberMarkupLanguage.Attributes;
using BetterSongSearch.Util;
using HMUI;
using System;
using System.Linq;
using System.Threading.Tasks;
using IPA.Utilities;
using TMPro;

namespace BetterSongSearch.UI.SplitViews {
	class UploadDetails {
		public static readonly UploadDetails instance = new UploadDetails();
		UploadDetails() { }

		[UIComponent("selectedCharacteristics")] readonly TextMeshProUGUI selectedCharacteristics = null;
		[UIComponent("selectedSongKey")] readonly TextMeshProUGUI selectedSongKey = null;
		[UIComponent("selectedSongDescription")] readonly CurvedTextMeshPro selectedSongDescription = null;
		[UIComponent("selectedRating")] readonly TextMeshProUGUI selectedRating = null;
		//[UIComponent("selectedDownloadCount")] TextMeshProUGUI selectedDownloadCount = null;
		[UIComponent("songDetailsLoading")] readonly ImageView songDetailsLoading = null;

		int revision;
		public async void Populate(SongSearchSong selectedSong) {
			var request = ++revision;
			var token = BSSFlowCoordinator.closeCancelSource.Token;
			var loader = BSSFlowCoordinator.assetLoader;
			var song = selectedSong.detailsSong;
			selectedSongDescription.text = "Loading...";
			songDetailsLoading.gameObject.SetActive(true);
			try {
				var info = await Task.Run(() => (
					Characteristics: String.Join(", ", song.difficulties.GroupBy(x => x.characteristic).Select(x => $"{x.Count()}x {x.Key}")),
					Key: song.key, Rating: song.rating.ToString("0.0%")), token);
				await UnityGame.SwitchToMainThreadAsync();
				if(request != revision || token.IsCancellationRequested || BSSFlowCoordinator.isClosing) return;
				selectedCharacteristics.text = info.Characteristics;
				selectedSongKey.text = info.Key;
				selectedRating.text = info.Rating;
				string desc;
				try {
					desc = await loader.GetSongDescription(info.Key, token);
				} catch(OperationCanceledException) when(token.IsCancellationRequested) {
					return;
				} catch {
					desc = "Failed to load description";
				}
				await UnityGame.SwitchToMainThreadAsync();
				if(request != revision || token.IsCancellationRequested || BSSFlowCoordinator.isClosing) return;
				songDetailsLoading.gameObject.SetActive(false);
				selectedSongDescription.text = desc;
				selectedSongDescription.gameObject.SetActive(false);
				selectedSongDescription.gameObject.SetActive(true);
			} catch(OperationCanceledException) when(token.IsCancellationRequested) {
			} catch(Exception ex) {
				Plugin.Log.Warn($"Preparing song details failed: {ex}");
			}
		}

	}
}
