using BetterSongSearch.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SongDetailsCache.Structs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BetterSongSearch.Util {
	class SongAssetAsyncLoader : IDisposable {
		const int MaxCoverCache = 32;
		const int MaxPreviewCache = 16;
		readonly Dictionary<uint, Sprite> _spriteCache = new Dictionary<uint, Sprite>();
		readonly Dictionary<uint, AudioClip> _previewCache = new Dictionary<uint, AudioClip>();
		readonly Queue<uint> _coverOrder = new Queue<uint>();
		readonly Queue<uint> _previewOrder = new Queue<uint>();
		bool _disposed;

		static void DestroyCover(Sprite sprite) {
			GameObject.Destroy(sprite.texture);
			GameObject.Destroy(sprite);
		}

		async Task<string> ApiRequest(string key, Func<JObject, string> valueGetter, CancellationToken token) {
			var baseUrl = PluginConfig.Instance.apiUrlOverride;

			if(baseUrl.Length == 0)
				baseUrl = BeatSaverRegionManager.detailsDownloadUrl;

			var c = await UnityWebrequestWrapper.DownloadBytes($"{baseUrl}/{key.ToLowerInvariant()}", token);
			
			return await Task.Run(() => {
				using(var jsonReader = new JsonTextReader(new StreamReader(new MemoryStream(c)))) {
					var ser = new JsonSerializer();
					return valueGetter(ser.Deserialize<JObject>(jsonReader));
				}
			}, token);
		}

		public Task<string> GetSongDescription(string key, CancellationToken token) {
			return ApiRequest(key, (x) => x.GetValue("description").Value<string>(), token);
		}
		public Task<string> GetPreviewURL(string key, CancellationToken token) {
			return ApiRequest(key, (x) => x.SelectToken("versions[0].coverURL", false).Value<string>(), token);
		}

		public async Task<Sprite> LoadCoverAsync(Song song, CancellationToken token) {
			var mid = song.mapId;

			if(_spriteCache.TryGetValue(mid, out Sprite sprite))
				return sprite;

			var path = PluginConfig.Instance.coverUrlOverride;

			if(path.Length == 0)
				path = BeatSaverRegionManager.coverDownloadUrl;

			path += $"/{song.hash.ToLowerInvariant()}.jpg";

			var cover = await UnityWebrequestWrapper.DownloadSprite(path, token);

			if(cover != null) {
				if(token.IsCancellationRequested || _disposed) {
					DestroyCover(cover);
					return SongCore.Loader.defaultCoverImage;
				}
				_spriteCache[mid] = cover;
				_coverOrder.Enqueue(mid);
				while(_coverOrder.Count > MaxCoverCache) {
					var oldId = _coverOrder.Dequeue();
					DestroyCover(_spriteCache[oldId]);
					_spriteCache.Remove(oldId);
				}
				return cover;
			}

			return SongCore.Loader.defaultCoverImage;
		}

		public async Task<AudioClip> LoadPreviewAsync(Song song, CancellationToken token) {
			var mid = song.mapId;

			if(_previewCache.TryGetValue(mid, out AudioClip ac))
				return ac;

			var path = PluginConfig.Instance.previewUrlOverride;

			if(path.Length == 0)
				path = BeatSaverRegionManager.previewDownloadUrl;

			path += $"/{song.hash.ToLowerInvariant()}.mp3";

			var preview = await UnityWebrequestWrapper.DownloadAudio(path, token, AudioType.MPEG);

			if(preview != null && preview.loadState == AudioDataLoadState.Loaded) {
				if(token.IsCancellationRequested || _disposed) {
					GameObject.Destroy(preview);
					return null;
				}
				_previewCache[mid] = preview;
				_previewOrder.Enqueue(mid);
				while(_previewOrder.Count > MaxPreviewCache) {
					var oldId = _previewOrder.Dequeue();
					GameObject.Destroy(_previewCache[oldId], 5f);
					_previewCache.Remove(oldId);
				}
				return preview;
			}
			if(preview != null)
				GameObject.Destroy(preview);

			return null;
		}

		public void Dispose() {
			_disposed = true;
			foreach(var s in _spriteCache.Values) {
				GameObject.DestroyImmediate(s.texture);
				GameObject.DestroyImmediate(s);
			}
			_spriteCache.Clear();
			_coverOrder.Clear();

			foreach(var x in _previewCache.Values)
				GameObject.Destroy(x);

			_previewCache.Clear();
			_previewOrder.Clear();
		}
	}
}
