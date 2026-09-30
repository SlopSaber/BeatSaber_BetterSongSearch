using System;
using System.Threading;
using System.Threading.Tasks;
using BetterSongSearch.UI;

namespace BetterSongSearch.Util {
	static class BeatSaverRegionManager {
		public const string mapDownloadUrlFallback = "https://cdn.beatsaver.com";
		public const string detailsDownloadUrl = "https://api.beatsaver.com/maps/id";

		public static string mapDownloadUrl { get; private set; } = "https://r2cdn.beatsaver.com";
		public static string coverDownloadUrl { get; private set; } = "https://cdn.beatsaver.com";
		public static string previewDownloadUrl { get; private set; } = "https://cdn.beatsaver.com";

		static bool didTheThing = false;

		public static async void RegionLookup(bool force = false) {
			if(didTheThing && !force)
				return;

			didTheThing = true;

			var loader = BSSFlowCoordinator.assetLoader;
			try {
				var joe = await loader.GetPreviewURL("225eb", CancellationToken.None);

				if(joe != null && joe.Length > 0) {
					var baseUrl = await Task.Run(() => {
						var u = new Uri(joe);
						return $"{u.Scheme}://{u.Host}";
					});
					coverDownloadUrl = previewDownloadUrl = baseUrl;
					return;
				}
			} catch { }
			didTheThing = false;
		}
	}
}
