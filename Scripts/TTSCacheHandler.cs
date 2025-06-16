using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace T3FL.TTS.Utils
{
	/// <summary>
	/// Support class that can be used in combination with any object that has TTS functionality,
	/// in order to manage its audio. Usually used as a member of <see cref="ITTSInputSource"/>,
	/// which also has a <see cref="TTSSpeakerController"/>.
	/// <br/><br/>
	/// WARNING: In order for this to work, <see cref="Initialize"/> should be called by the object
	/// that uses this <see cref="TTSCacheHandler"/> before any usage of this class.
	/// </summary>
	public class TTSCacheHandler
	{
		/// <summary>
		/// See <see cref="ITTSInputSource.CurrentTTS"/>.
		/// </summary>
		public IEnumerable<(AudioClip Audio, float EndDelay)> CurrentTTSCache
		{
			get
			{
				UpdateCache();
				foreach (var audioComponent in _currentTTSCache)
				{
					if (audioComponent.Data is AudioClip ac) // this is a single audio clip
					{
						// apply delay only if this isn't the last audio component in the cache
						var endDelay = audioComponent != _currentTTSCache.Last() ?
							audioComponent.EndDelay : 0;
						yield return (ac, endDelay);
						UpdateCache();
					}
					else if (audioComponent.Data is List<AudioClip> clips) // this is a cluster
					{
						for (int i = 0; i < clips.Count; i++)
						{
							// apply delay only if this is the last audio clip of the
							// cluster, but not the last audio component
							var endDelay = i + 1 == clips.Count && audioComponent != _currentTTSCache.Last() ?
								audioComponent.EndDelay : 0;

							yield return (clips[i], audioComponent.EndDelay);
							UpdateCache();
						}
					}
				}
			}
		}

		/// <summary>
		/// The audio clips that have been loaded for the current state of the object
		/// that uses this <see cref="TTSCacheHandler"/> (usually a <see cref="ITTSInputSource"/>).
		/// If it's not complete for the current state, it will get updated every time and audio 
		/// clip is used.
		/// </summary>
		private List<TTSAudioComponent> _currentTTSCache = new();
		/// <summary>
		/// Contains the IDs of the relevant text that is currently visible in the object
		/// that uses this <see cref="TTSCacheHandler"/> (usually a <see cref="ITTSInputSource"/>).
		/// </summary>
		private List<(string ClipId, float EndDelay)> _currentClipIds;
		/// <summary>
		/// Becomes <i>true</i> when all the audio clips in <see cref="_currentClipIds"/> have finished
		/// downloading and have been added to <see cref="_currentTTSCache"/>.
		/// </summary>
		private bool _isCacheComplete = false;
		/// <summary>
		/// Used in order to populate the <see cref="_currentClipIds"/> list.
		/// <br/><br/>
		/// WARNING: the object that uses this <see cref="TTSCacheHandler"/> should call 
		/// <see cref="Initialize"/> in order to set this function, before any usage of this class.
		/// </summary>
		private Func<List<(string ClipId, float EndDelay)>> _getCurrentClipIds;

		/// <summary>
		/// <paramref name="GetCurrentClipIds"/> should return the IDs of the relevant text that
		/// is currently visible on the object that uses this <see cref="TTSCacheHandler"/>.
		/// <br/><br/>
		/// The <i>EndDelay</i> float refers to the amount of time that should be waited
		/// for at the end of the current clip and before playing the next one.
		/// </summary>
		/// <param name="GetCurrentClipIds"></param>
		public void Initialize(Func<List<(string ClipId, float EndDelay)>> GetCurrentClipIds)
		{
			_getCurrentClipIds = GetCurrentClipIds;
		}

		public void ClearCache()
		{
			_isCacheComplete = false;
			_currentClipIds = null;
			_currentTTSCache.Clear();
		}

		private void UpdateCache()
		{
			if (_isCacheComplete) return; // only update if necessary

			if (_currentClipIds == null)
			{
				if (_getCurrentClipIds == null)
				{
					Debug.Log("The delegate that aquires the current clip Ids hasn't been initialized. Use " + nameof(Initialize) + " before using the cache handler.");
				}

				// get the IDs of the clips that are currently being displayed in the UI
				_currentClipIds = _getCurrentClipIds();
			}

			// get each available clip from the TTS manager

			for (int i = 0; i < _currentClipIds.Count; i++)
			{
				var (clipId, endDelay) = _currentClipIds[i];

				if (_currentTTSCache.Count < i + 1)
				{
					_currentTTSCache.Add(TTSAudioComponent.GetOrCreate(clipId, endDelay));
				}

				var audioComponent = _currentTTSCache[i];

				if (audioComponent.IsComplete) continue;

				if (!TTSManager.IsReady(clipId) ||
					(!TTSManager.IsCluster(clipId) && !TTSManager.IsClusterAvailable(clipId)))
				{
					// the clip hasn't been processed yet
					Debug.LogError("The clip with ID " + clipId + " hasn't been processed yet.");
					continue;
				}

				if (TTSManager.IsCluster(clipId))
				{
					// the text with this ID had to be split into multiple audio clips due to
					// its large size
					var cluster = TTSManager.GetClusterObject(clipId);

					audioComponent.Data = cluster.GetClipList();
					audioComponent.IsComplete = cluster.IsComplete;
					audioComponent.ExpectedCount = cluster.ExpectedClipCount;
				}
				else
				{
					// single clip, just add to list
					audioComponent.Data = TTSManager.GetClip(clipId);
					audioComponent.IsComplete = true;
				}
			}

			// if all clips have been downloaded, then no need to
			// go through this process until the UI changes again
			_isCacheComplete = _currentTTSCache.Where(c => c.IsComplete).Count() == _currentClipIds.Count;
		}

		/// <summary>
		/// Handles the audio clip or cluster for a single ID. The IDs are provided by the 
		/// <see cref="_currentClipIds"/> list.
		/// </summary>
		private class TTSAudioComponent
		{
			/// <summary>
			/// Is <i>true</i> when <see cref="Data"/> is either an <see cref="AudioClip"/> object, 
			/// or when it is a cluster (list of audio clips) and its count is equal to 
			/// <see cref="ExpectedCount"/>.
			/// </summary>
			public bool IsComplete { get; set; }
			/// <summary>
			/// If <see cref="Data"/> is an <see cref="AudioClip"/> object then the value is 1, else 
			/// if <see cref="Data"/> is a cluster, then the value is the same as its corresponding 
			/// <see cref="TTSManager.TTSAudioClipCluster.ExpectedClipCount"/> .
			/// </summary>
			public int ExpectedCount { get; set; }
			/// <summary>
			/// The amount of time that should be waited for at the end of the <see cref="Data"/> 
			/// and before playing the contents of the next <see cref="Data"/>.
			/// </summary>
			public readonly float EndDelay;
			/// <summary>
			/// Contains a single audio clip, or a list of audio clips. 
			/// </summary>
			public object Data { get; set; }

			private TTSAudioComponent(float endDelay)
			{
				EndDelay = endDelay;
			}

			#region CACHE
			/// <summary>
			/// Cache the <see cref="TTSAudioComponent"/> objects in order to avoid unnecessarily
			/// instantiating new ones over and over again, for the same contents. The key represents 
			/// the ID of the audio clip or cluster.
			/// </summary>
			private static Dictionary<string, TTSAudioComponent> _cache = new();

			/// <summary>
			/// If a <see cref="TTSAudioComponent"/> has been instantiated before
			/// for the given <see cref="ID"/>, it will be reused, else it will be instantiated.
			/// The <paramref name="endDelay"/> parameter refers to <see cref="EndDelay"/>.
			/// </summary>
			/// <param name="ID"></param>
			/// <param name="endDelay"></param>
			/// <returns></returns>
			public static TTSAudioComponent GetOrCreate(string ID, float endDelay)
			{
				if (!_cache.ContainsKey(ID))
				{
					_cache.Add(ID, new TTSAudioComponent(endDelay));
				}

				return _cache[ID];
			}
			#endregion
		}
	}
}