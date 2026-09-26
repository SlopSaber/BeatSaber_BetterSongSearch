using BeatSaberMarkupLanguage;
using BetterSongSearch.Util;
using HMUI;
using Legato;
using SongDetailsCache;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterSongSearch.UI.DownloadHistoryView;

namespace BetterSongSearch.UI {
	class BSSFlowCoordinator : FlowCoordinator {
		internal static FilterView filterView;
		internal static SongListController songListView;
		internal static DownloadHistoryView downloadHistoryView;

		internal static SongAssetAsyncLoader assetLoader = null;
		static internal SongDetails songDetails = null;

		static BSSFlowCoordinator instance = null;
		static IDisposable songLoadedSubscription;
		internal static readonly SemaphoreSlim dataProcessingSlot = new SemaphoreSlim(1, 1);
		static int filterRevision;
		static int datasetRevision;
		internal static bool isClosing { get; private set; }

		internal static void DisposeSongCoreSubscription() {
			songLoadedSubscription?.Dispose();
			songLoadedSubscription = null;
		}

		public static CancellationTokenSource closeCancelSource;

		public static SongSearchSong[] songsList { get; private set; } = null;
		public static SongSearchSong[] filteredSongsListPreallocatedArray { get; private set; } = null;
		public static SongSearchSong[] searchedSongsListPreallocatedArray { get; private set; } = null;

		public static PlayerDataModel playerDataModel = null;
		public static Dictionary<string, Dictionary<string, float>> _songsWithScores = null;
		public static bool songsWithScoresShouldProbablyUpdate = true;

		/*
		 * With the control flow of BSS, this is always accessed off-main-thread when it does happen
		 * to get populated / updated, so we can keep this on whatever thread it happens to get called from
		 */
		public static Dictionary<string, Dictionary<string, float>> songsWithScores {
			get {
				if(_songsWithScores == null || songsWithScoresShouldProbablyUpdate) {
					_songsWithScores ??= new Dictionary<string, Dictionary<string, float>>();
					songsWithScoresShouldProbablyUpdate = false;

					foreach(var x in playerDataModel.playerData.levelsStatsData) {
						var lid = x.Key.levelId;
						if(!x.Value.validScore || x.Value.highScore == 0 || lid.Length < 13 + 40 || !lid.StartsWith(CustomLevelLoader.kCustomLevelPrefixId, StringComparison.Ordinal))
							continue;

						var sh = lid.Substring(13, 40);

						SongDetailsCache.Structs.Song song;
						// local score level id's can be scuffed if you pass custom levels w/ no songcore installed
						try {
							if(!songDetails.songs.FindByHash(sh, out song))
								continue;
						} catch { continue; }

						if(!song.GetDifficulty(out var diff, (SongDetailsCache.Structs.MapDifficulty)x.Key.difficulty))
							continue;

						if(!_songsWithScores.TryGetValue(sh, out var h))
							_songsWithScores.Add(sh, h = new Dictionary<string, float>());

						/*
						 * Calculating the maxscore for a map is now a bajillion times more complex than it was before
						 * bye bye sort by worst score
						 */
						//var maxScore = ScoreModel.MaxRawScoreForNumberOfNotes((int)diff.notes);
						//h[$"{x.beatmapCharacteristic.serializedName}_{x.difficulty}"] = (x.highScore * 100f) / maxScore;
				h[$"{x.Key.characteristic.SerializedName()}_{x.Key.difficulty}"] = 0;
					}
				}

				return _songsWithScores;
			}
		}

		public async override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling) {
			instance = this;
			isClosing = false;

			closeCancelSource = new CancellationTokenSource();

			assetLoader ??= new SongAssetAsyncLoader();

			playerDataModel ??= XD.FunnyMono(playerDataModel) ?? UnityEngine.Object.FindFirstObjectByType<PlayerDataModel>();

			static async Task DataUpdated() {
				var revision = Interlocked.Increment(ref datasetRevision);
				var details = songDetails;
				if(instance == null || isClosing || details == null)
					return;
				await IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() => songListView?.CancelSearch());
				_ = IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() => {
					if(revision == datasetRevision && !isClosing && details.songs.Length > 0)
						filterView?.datasetInfoLabel?.SetText($"{details.songs.Length} songs in dataset | Newest: {details.songs.Last().uploadTime.ToLocalTime():d\\. MMM yy - HH:mm}");
				});

			await dataProcessingSlot.WaitAsync();
			try {
				if(revision != datasetRevision || isClosing)
					return;
				SongListController.filteredSongsList = null;
				SongListController.searchedSongsList = null;
				var nextSongs = await Task.Run(() => {
					var result = new SongSearchSong[details.songs.Length];
					for(var i = 0; i < result.Length; i++) {
						if((i & 255) == 0 && revision != datasetRevision)
							return null;
						result[i] = new SongSearchSong(details.songs[i], details);
					}
					return result;
			});
				if(nextSongs == null || revision != datasetRevision || isClosing)
					return;
				songsList = nextSongs;
				filteredSongsListPreallocatedArray = new SongSearchSong[nextSongs.Length];
				searchedSongsListPreallocatedArray = new SongSearchSong[nextSongs.Length];
			} finally {
				dataProcessingSlot.Release();
			}

				_ = IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(FilterSongs);
			};

			if(firstActivation) {
				SetTitle("Better Song Search");

				showBackButton = true;

				filterView = BeatSaberUI.CreateViewController<FilterView>();
				songListView = BeatSaberUI.CreateViewController<SongListController>();
				downloadHistoryView = BeatSaberUI.CreateViewController<DownloadHistoryView>();

				ProvideInitialViewControllers(songListView, filterView, downloadHistoryView);

				songLoadedSubscription = SongCoreLoaderEvents.SubscribeToSongsLoaded(SongcoreSongsLoaded);
				SongDetailsContainer.dataAvailableOrUpdated += () => _ = DataUpdated();

				BeatSaverRegionManager.RegionLookup();
			}
			// Re-Init every time incase its time to download a new database
			songDetails = await SongDetails.Init(1);

			await DataUpdated();

			if(!firstActivation)
				downloadHistoryView.RefreshTable();
		}

		void SongcoreSongsLoaded() {
			foreach(var x in downloadHistoryView.downloadList)
				if(x.status == DownloadHistoryEntry.DownloadStatus.Downloaded)
					x.status = DownloadHistoryEntry.DownloadStatus.Loaded;

			downloadHistoryView.RefreshTable(false);

			songListView.selectedSongView.PlayQueuedSongToPlay();
		}

		static internal int lastVisibleTableRowIdx { get; private set; } = 0;

		static Action cancelConfirmCallback = null;
		public static bool ConfirmCancelOfPending(Action confirmCallback) {
			if(downloadHistoryView.downloadList.Any(x => x.isDownloading || x.isQueued)) {
				cancelConfirmCallback = confirmCallback;
				songListView.ShowCloseConfirmation();

				return true;
			}
			return false;
		}

		public static void ConfirmCancelCallback(bool doCancel = true) {
			if(doCancel) {
				foreach(var x in downloadHistoryView.downloadList) {
					if(!x.isDownloading && !x.isQueued)
						continue;

					x.retries = 69;
					x.status = DownloadHistoryEntry.DownloadStatus.Failed;
				}
				closeCancelSource?.Cancel();

				cancelConfirmCallback?.Invoke();
			}

			cancelConfirmCallback = null;
		}

		/// <summary>
		/// Cloases the BetterSongSearch Flow
		/// </summary>
		/// <param name="immediately">True = Close immediately without transition</param>
		/// <param name="downloadAbortConfim">True = Confirm closing if there is pending downloads</param>
		public static void Close(bool immediately = false, bool downloadAbortConfim = true) {
			if(instance == null || !instance.isActivated)
				return;

			if(downloadAbortConfim && ConfirmCancelOfPending(() => Close(immediately, false)))
				return;

			cancelConfirmCallback = null;
			isClosing = true;
			Interlocked.Increment(ref datasetRevision);
			Interlocked.Increment(ref filterRevision);
			closeCancelSource?.Cancel();
			songListView?.CancelSearch();
			SelectedSongView.songAssetLoadCanceller?.Cancel();
			try {
				XD.FunnyMono(SelectedSongView.songPreviewPlayer)?.CrossfadeToDefault();
			} catch { }

			foreach(var x in songListView.GetComponentsInChildren<ModalView>()) {
				x.Hide(false);
				//x.gameObject.SetActive(false);
			}

			if(downloadHistoryView.hasUnloadedDownloads)
				SongCore.Loader.Instance.RefreshSongs(false);

			Manager._parentFlow.DismissFlowCoordinator(instance, () => {
				lastVisibleTableRowIdx = songListView.songList.GetVisibleCellsIdRange().Item1;
				songsList = null;
				filteredSongsListPreallocatedArray = null;
				searchedSongsListPreallocatedArray = null;
				SongListController.filteredSongsList = null;
				SongListController.searchedSongsList = null;
				WeightedSongSearch.cachedSearchableStrings = null;
				songsWithScoresShouldProbablyUpdate = true;
				songListView.songList.ReloadData();

				if(songListView?.selectedSongView?.coverImage != null)
					songListView.selectedSongView.coverImage.sprite = SongCore.Loader.defaultCoverImage;
				assetLoader?.Dispose();
				assetLoader = null;

				instance = null;
			}, ViewController.AnimationDirection.Horizontal, immediately);
		}

		public override void BackButtonWasPressed(ViewController topViewController) => Close();

		public static async void FilterSongs() {
			var revision = Interlocked.Increment(ref filterRevision);
			if(isClosing || songDetails == null || songsList == null)
				return;
			songListView?.CancelSearch();

#if DEBUG
			var sw = new System.Diagnostics.Stopwatch();
			sw.Start();
#endif

			await dataProcessingSlot.WaitAsync();
			try {
				if(isClosing || revision != filterRevision || songsList == null || filteredSongsListPreallocatedArray == null)
					return;
			var sourceSongs = songsList;
			var sourceDetails = songDetails;
			var output = filteredSongsListPreallocatedArray;
			var selectedFilter = FilterView.currentFilter.Clone();
			selectedFilter.CalculateTagBitfields();
			var sortMode = SongListController.selectedSortMode;
			var count = await Task.Run(() => {
				var sc = 0;

				// Loop through our (custom) songdetails array
				for(var i = 0; i < sourceSongs.Length; i++) {
					if((i & 255) == 0 && (revision != filterRevision || isClosing))
						break;
					/*
					 * Since our custom array is recreated whenever songDetails updates we can
					 * get the song directly by ref from songdetails as the index matches
					 */
					ref var val = ref sourceDetails.songs[i];

					// Check if the song itself passes the filter
					if(!filterView.SongCheck(in val, selectedFilter) || !filterView.SearchSongCheck(sourceSongs[i], selectedFilter, sortMode))
						continue;

					var hasAnyValid = false;

					/*
					 * loop all diffs of this song to see if any diff matches our filter.
					 * for those diffs that we checked we pre-set passesFilter so that it
					 * doesnt need to get (re)checked later whenever the diffs array is accessed
					 */
					var theThing = sourceSongs[i];

					for(var iDiff = 0; iDiff < val.diffCount; iDiff++) {
						var theDiff = theThing.diffs[iDiff];

						theDiff._passesFilter = filterView.DifficultyCheck(in theDiff.detailsDiff, selectedFilter) &&
							filterView.SearchDifficultyCheck(theDiff, selectedFilter, sortMode);
						if(!hasAnyValid)
							hasAnyValid = theDiff.passesFilter;
					}

					if(!hasAnyValid)
						continue;

					output[sc++] = theThing;
				}

				return sc;
			});
				if(isClosing || revision != filterRevision || output != filteredSongsListPreallocatedArray)
					return;
				SongListController.filteredSongsList = new ArraySegment<SongSearchSong>(output, 0, count);
			} catch(Exception ex) {
				Plugin.Log.Warn($"Filtering songs failed: {ex}");
				return;
			} finally {
				dataProcessingSlot.Release();
			}

#if DEBUG
			Plugin.Log.Info(string.Format("Filtering the songs took {0}ms", sw.Elapsed.TotalMilliseconds));
#endif

			songListView?.UpdateSearchedSongsList();
		}
	}
}
