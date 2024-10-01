using Lynx.JSON;
using Lynx.Utils;
using Meta.Voice.Audio;
using Meta.WitAi;
using Meta.WitAi.TTS;
using Meta.WitAi.TTS.Data;
using Meta.WitAi.TTS.Integrations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using T3FL.TTS.Constants;
using T3FL.TTS.Download;
using T3FL.TTS.Settings;
using UnityEngine;

namespace T3FL.TTS
{
	public class TTSManager : MonoBehaviour
	{
		public static TTSManager Instance { get; private set; }
		/// <summary>
		/// Is <i>true</i> if <see cref="Initialize"/> has been called at any point
		/// during runtime.
		/// </summary>
		public static bool IsInitialized { get; private set; } = false;
		/// <summary>
		/// If not null, the audio with this ID will be downloaded as soon as possible.
		/// <br/><br/>
		/// WARNING: the ID has to have accessibility markers applied (see 
		/// <see cref="StringProcessor.ApplyAccessibilityMarkers"/>).
		/// </summary>
		public static string HighPriorityAudioId => TTSDownloader.HighPriorityRequestId;

		private TTSDiskCache _ttsDiskCache
		{
			get
			{
				if (_ttsDiskCachePrefab == null)
				{
					_ttsDiskCachePrefab = GetComponentInChildren<TTSDiskCache>();
				}

				return _ttsDiskCachePrefab;
			}
		}
		private TTSService _ttsService
		{
			get
			{
				if (_ttsServicePrefab == null)
				{
					_ttsServicePrefab = GetComponentInChildren<TTSService>();
				}

				return _ttsServicePrefab;
			}
		}
		private TTSWit _ttsWit
		{
			get
			{
				if (_ttsWitPrefab == null)
				{
					_ttsWitPrefab = GetComponentInChildren<TTSWit>();
				}

				return _ttsWitPrefab;
			}
		}

		[SerializeField, Tooltip("Used in order to manually set the TTSDiskCache reference. " +
			"Can be left null if the script is attached to a child of the TTSManager object.")]
		private TTSDiskCache _ttsDiskCachePrefab;
		[SerializeField, Tooltip("Used in order to manually set the TTSService reference. " +
			"Can be left null if the script is attached to a child of the TTSManager object.")]
		private TTSService _ttsServicePrefab;
		[SerializeField, Tooltip("Used in order to manually set the TTSWit reference. " +
			"Can be left null if the script is attached to a child of the TTSManager object.")]
		private TTSWit _ttsWitPrefab;

		// data management
		/// <summary>
		/// Contains all the audio clips that are available to be used, categorized by speed (the key
		/// of the dictionary).
		/// </summary>
		private static Dictionary<int, ClipDataContainer> _ttsClipData = new();
		private static int _currentSpeedSetting => SoundSettingsManager.SoundSettings.Speed;

		// download
		/// <summary>
		/// Keeps track of all the request that have been received by this manager from other systems,
		/// (see <see cref="PrepareClip"/>), in order to avoid duplicates.
		/// </summary>
		private static List<string> _receivedRequestsIds = new List<string>();
		/// <summary>
		/// If the manager received a request before being initialized, add said request to this list
		/// and process it after initialization.
		/// </summary>
		private static List<TTSRequestData> _waitingList = new List<TTSRequestData>();

		private void Awake()
		{
			Instance = this;
			if (IsInitialized)
			{
				TTSDownloader.StartDownloadCoroutine();
				TTSDownloader.ReprocessAwaitingAudioExtractionClips();
			}
		}

		private void OnDestroy()
		{
			TTSDownloader.StopDownloadCoroutine();
		}

		/// <summary>
		/// Initializes the <see cref="TTSManager"/> and its helpers. After this method is called, it will begin 
		/// downloading any clips that were requested to be prepared (see <see cref="PrepareClip"/>)
		/// before being initialized.
		/// <br/><br/>
		/// The <paramref name="voiceSettingsPath"/> string refers to the path of the json file that contains
		/// the voice settings of all the characters that will speak. This json file should contain a
		/// <see cref="GenericSerializable{T}"/> of <see cref="List{T}"/> of <see cref="TTSVoiceModel"/> objects.
		/// </summary>
		/// <param name="downloadHelper"></param>
		/// <param name="voiceSettingsPath"></param>
		public static void Initialize(ITTSDownloadHelper downloadHelper, string voiceSettingsPath)
		{
			if (IsInitialized) return;

			TTSVoiceManager.Initialize(voiceSettingsPath);
			TTSDownloader.Initialize(downloadHelper);

			TTSDownloader.OnClipDownloaded += OnClipDownloaded;

			IsInitialized = true;

			if (_waitingList.Count > 0)
			{
				_waitingList.ForEach((clip) => RequestDownload(clip));
				_waitingList.Clear();
			}
		}

		/// <summary>
		/// Returns an audio clip for the given ID, if it has been processed and if
		/// the text that was provided for it hasn't been processed as a cluster.
		/// <br/><br/>
		/// Check if a given ID is a cluster by using <see cref="IsCluster"/>.
		/// <br/><br/>
		/// The optional parameters determine the accessibility settings of the cluster, as such:
		/// <list type="bullet">
		/// <item>if <paramref name="hasAccessibilitySettingsApplied"/> is <i>true</i> then it will extract the
		/// accessibility settings from the <paramref name="ID"/> (it must have been through 
		/// <see cref="StringProcessor.ApplyAccessibilityMarkers"/>)</item>
		/// <item>else if <paramref name="accessibilitySettings"/> is not null then it will use that
		/// in order to determine the accessibility settings</item>
		/// <item>else it will use the current settings (see 
		/// <see cref="SoundSettingsManager.SoundSettings"/>)</item>
		/// </list>
		/// </summary>
		/// <param name="ID"></param>
		/// <returns></returns>
		public static AudioClip GetClip(string ID, bool hasAccessibilitySettingsApplied = false, BaseSoundSettingsModel accessibilitySettings = null)
		{
			var (baseID, accessibility) = GetSettingsForID(ID, hasAccessibilitySettingsApplied, accessibilitySettings);
			return _ttsClipData[accessibility.Speed].GetClip(baseID, accessibility.IsMale);
		}

		/// <summary>
		/// Returns a list of audio clips for the given ID, if it has been processed and if
		/// the text that was provided for it was too long to be processed as a single audio clip.
		/// <br/><br/>
		/// Check if a given ID is a cluster by using <see cref="IsCluster"/>.
		/// <br/><br/>
		/// The optional parameters determine the accessibility settings of the clip, as such:
		/// <list type="bullet">
		/// <item>if <paramref name="hasAccessibilitySettingsApplied"/> is <i>true</i> then it will extract the
		/// accessibility settings from the <paramref name="ID"/> (it must have been through 
		/// <see cref="StringProcessor.ApplyAccessibilityMarkers"/>)</item>
		/// <item>else if <paramref name="accessibilitySettings"/> is not null then it will use that
		/// in order to determine the accessibility settings</item>
		/// <item>else it will use the current settings (see 
		/// <see cref="SoundSettingsManager.SoundSettings"/>)</item>
		/// </list>
		/// </summary>
		/// <param name="ID"></param>
		/// <returns></returns>
		public static List<AudioClip> GetClipCluster(string ID, bool hasAccessibilitySettingsApplied = false, BaseSoundSettingsModel accessibilitySettings = null)
		{
			var (baseID, accessibility) = GetSettingsForID(ID, hasAccessibilitySettingsApplied, accessibilitySettings);
			return _ttsClipData[accessibility.Speed].GetClipCluster(baseID, accessibility.IsMale);
		}

		/// <summary>
		/// Returns a <see cref="TTSAudioClipCluster"/> for the given ID, if it has been processed and 
		/// if the text that was provided for it was too long to be processed as a single audio clip.
		/// <br/><br/>
		/// Check if a given ID is a cluster by using <see cref="IsCluster"/>.
		/// <br/><br/>
		/// The optional parameters determine the accessibility settings of the clip, as such:
		/// <list type="bullet">
		/// <item>if <paramref name="hasAccessibilitySettingsApplied"/> is <i>true</i> then it will extract the
		/// accessibility settings from the <paramref name="ID"/> (it must have been through 
		/// <see cref="StringProcessor.ApplyAccessibilityMarkers"/>)</item>
		/// <item>else if <paramref name="accessibilitySettings"/> is not null then it will use that
		/// in order to determine the accessibility settings</item>
		/// <item>else it will use the current settings (see 
		/// <see cref="SoundSettingsManager.SoundSettings"/>)</item>
		/// </list>
		/// </summary>
		/// <param name="ID"></param>
		/// <param name="hasAccessibilitySettingsApplied"></param>
		/// <param name="accessibilitySettings"></param>
		/// <returns></returns>
		public static TTSAudioClipCluster GetClusterObject(string ID, bool hasAccessibilitySettingsApplied = false, BaseSoundSettingsModel accessibilitySettings = null)
		{
			var (baseID, accessibility) = GetSettingsForID(ID, hasAccessibilitySettingsApplied, accessibilitySettings);
			return _ttsClipData[accessibility.Speed].GetClusterObject(baseID, accessibility.IsMale);
		}

		/// <summary>
		/// Returns true if the text that was provided for the given ID is long enough to have been
		/// processed as a cluster (see <see cref="GetClipCluster"/>). 
		/// <br/><br/>
		/// The optional parameters determine the accessibility settings of the cluster, as such:
		/// <list type="bullet">
		/// <item>if <paramref name="hasAccessibilitySettingsApplied"/> is <i>true</i> then it will extract the
		/// accessibility settings from the <paramref name="ID"/> (it must have been through 
		/// <see cref="StringProcessor.ApplyAccessibilityMarkers"/>)</item>
		/// <item>else if <paramref name="accessibilitySettings"/> is not null then it will use that
		/// in order to determine the accessibility settings</item>
		/// <item>else it will use the current settings (see 
		/// <see cref="SoundSettingsManager.SoundSettings"/>)</item>
		/// </list>
		/// </summary>
		/// <param name="ID"></param>
		/// <returns></returns>
		public static bool IsCluster(string ID, bool hasAccessibilitySettingsApplied = false, BaseSoundSettingsModel accessibilitySettings = null)
		{
			var (baseID, accessibility) = GetSettingsForID(ID, hasAccessibilitySettingsApplied, accessibilitySettings);
			return _ttsClipData.ContainsKey(accessibility.Speed) && _ttsClipData[accessibility.Speed].IsCluster(baseID);
		}

		/// <summary>
		/// Returns true if the clip with the given <paramref name="ID"/> has been processed
		/// successfully and is available through <see cref="GetClip"/> or
		/// <see cref="GetClipCluster"/>.
		/// <br/><br/>
		/// The optional parameters determine the accessibility settings of the clip, as such:
		/// <list type="bullet">
		/// <item>if <paramref name="hasAccessibilitySettingsApplied"/> is <i>true</i> then it will extract the
		/// accessibility settings from the <paramref name="ID"/> (it must have been through 
		/// <see cref="StringProcessor.ApplyAccessibilityMarkers"/>)</item>
		/// <item>else if <paramref name="accessibilitySettings"/> is not null then it will use that
		/// in order to determine the accessibility settings</item>
		/// <item>else it will use the current settings (see 
		/// <see cref="SoundSettingsManager.SoundSettings"/>)</item>
		/// </list>
		/// </summary>
		/// <param name="ID"></param>
		/// <returns></returns>
		public static bool IsReady(string ID, bool hasAccessibilitySettingsApplied = false, BaseSoundSettingsModel accessibilitySettings = null)
		{
			var (baseID, accessibility) = GetSettingsForID(ID, hasAccessibilitySettingsApplied, accessibilitySettings);
			return _ttsClipData.ContainsKey(accessibility.Speed) &&
				_ttsClipData[accessibility.Speed].IsReady(baseID, accessibility.IsMale);
		}

		/// <summary>
		/// Returns true if the cluster with the given <paramref name="ID"/> has at least
		/// one <see cref="AudioClip"/> that has been downloaded (available through
		/// <see cref="GetClipCluster"/>).
		/// <br/><br/>
		/// The optional parameters determine the accessibility settings of the clip, as such:
		/// <list type="bullet">
		/// <item>if <paramref name="hasAccessibilitySettingsApplied"/> is <i>true</i> then it will extract the
		/// accessibility settings from the <paramref name="ID"/> (it must have been through 
		/// <see cref="StringProcessor.ApplyAccessibilityMarkers"/>)</item>
		/// <item>else if <paramref name="accessibilitySettings"/> is not null then it will use that
		/// in order to determine the accessibility settings</item>
		/// <item>else it will use the current settings (see 
		/// <see cref="SoundSettingsManager.SoundSettings"/>)</item>
		/// </list>
		/// </summary>
		/// <param name="ID"></param>
		/// <param name="hasAccessibilitySettingsApplied"></param>
		/// <param name="accessibilitySettings"></param>
		/// <returns></returns>
		public static bool IsClusterAvailable(string ID, bool hasAccessibilitySettingsApplied = false, BaseSoundSettingsModel accessibilitySettings = null)
		{
			var (baseID, accessibility) = GetSettingsForID(ID, hasAccessibilitySettingsApplied, accessibilitySettings);
			return _ttsClipData.ContainsKey(accessibility.Speed) &&
				_ttsClipData[accessibility.Speed].IsClusterAvailable(baseID, accessibility.IsMale);
		}

		/// <summary>
		/// Prepares a string to be downloaded as an audio clip. The <paramref name="ID"/> parameter
		/// has to be unique.
		/// <br/><br/>
		/// The <paramref name="hasAccessibilitySettingsApplied"/> parameter should only be <i>true</i> if 
		/// the ID has already been put through <see cref="StringProcessor.ApplyAccessibilityMarkers"/>.
		/// <br/><br/>
		/// If <paramref name="processImmediately"/> is set to <i>true</i>, then the given audio clip will
		/// take priority over all the other ones in the queue.
		/// </summary>
		/// <param name="ID"></param>
		/// <param name="text"></param>
		public static void PrepareClip(string ID, string text, string speaker, bool hasAccessibilitySettingsApplied = false, bool processImmediately = false)
		{
			if (!hasAccessibilitySettingsApplied)
			{
				bool? isMale = speaker == TTSConstants.PLAYER ?
							SoundSettingsManager.SoundSettings.IsMale : null;
				ID = StringProcessor.ApplyAccessibilityMarkers(ID, _currentSpeedSetting, isMale);
			}

			// if it's a high priority request, only set it if it hasn't been processed already
			if (processImmediately && TTSDownloader.HighPriorityRequestId != ID && !IsReady(ID, true))
			{
				TTSDownloader.HighPriorityRequestId = ID;
			}

			// no need to download it if it has already been received to be processed
			if (_receivedRequestsIds.Contains(ID))
			{
				return;
			}

			_receivedRequestsIds.Add(ID);

			text = StringProcessor.RemoveFormatting(text);

			if (!StringProcessor.ContainsSpeech(text))
			{
				// the text contains no actual words that can be spoken. most likely it
				// contains something like ". . .", so add it as a null audio anyway so
				// that it can be used to create a pause during the dialog

				AddClip(ID, null);
				Debug.LogWarning("Text with ID " + ID + " does not contain any actual words that can be spoken." +
					"It will be added by default as null.");

				return;
			}

			// if the manager hasn't been initialized, wait until it is
			if (!IsInitialized)
			{
				_waitingList.Add(new TTSRequestData { ID = ID, Text = text, Speaker = speaker });
				return;
			}

			// if everything is fine, go on with downloading the clip
			RequestDownload(ID, text, speaker);
		}

		/// <summary>
		/// Prepares a text to be downloaded, and passes it to the <see cref="TTSDownloader"/>.
		/// </summary>
		/// <param name="ID"></param>
		/// <param name="text"></param>
		private static void RequestDownload(string ID, string text, string speaker)
		{
			if (!StringProcessor.IsCluster(text))
			{
				RequestDownload(new TTSRequestData { ID = ID, Text = text, Speaker = speaker });
			}
			else
			{
				var cluster = StringProcessor.GetCluster(ID, text);
				TTSDownloader.AddClusterToWaitingList(ID, cluster, speaker);
			}
		}

		/// <summary>
		/// Prepares a text to be downloaded, and passes it to the <see cref="TTSDownloader"/>.
		/// </summary>
		/// <param name="request"></param>
		private static void RequestDownload(TTSRequestData request)
		{
			if (!StringProcessor.IsCluster(request.Text))
			{
				request.Text = StringProcessor.RemoveOmittedCharacters(request.Text);
				TTSVoiceManager.ApplyVoiceStyle(ref request);
				TTSDownloader.AddToWaitingList(request);
			}
			else
			{
				var cluster = StringProcessor.GetCluster(request.ID, request.Text);
				TTSDownloader.AddClusterToWaitingList(request.ID, cluster, request.Speaker);
			}
		}

		/// <summary>
		/// Receives an ID and its accessibility markers in all possible forms, and returns the base ID (no 
		/// accessibility markers) and the <see cref="BaseSoundSettingsModel"/> that corresponds to it.
		/// </summary>
		/// <param name="ID"></param>
		/// <param name="hasAccessibilitySettingsApplied"></param>
		/// <param name="accessibilitySettings"></param>
		/// <returns></returns>
		private static (string ID, BaseSoundSettingsModel soundSettings) GetSettingsForID(string ID, bool hasAccessibilitySettingsApplied, BaseSoundSettingsModel accessibilitySettings)
		{
			var id = hasAccessibilitySettingsApplied ? ID : StringProcessor.RemoveAccessibilityMarkers(ID);

			if (hasAccessibilitySettingsApplied)
			{
				return (id, StringProcessor.ExtractAccessibilitySettings(ID));
			}
			else if (accessibilitySettings != null)
			{
				return (id, accessibilitySettings);
			}
			else
			{
				return (id, SoundSettingsManager.SoundSettings);
			}
		}

		/// <summary>
		/// Stores an <see cref="AudioClip"/> for the given <paramref name="ID"/>.
		/// </summary>
		/// <param name="ID"></param>
		/// <param name="audioClip"></param>
		private static void OnClipDownloaded(string ID, AudioClip audioClip)
		{
			AddClip(ID, audioClip);
		}

		/// <summary>
		/// Stores the given <paramref name="cluster"/>, so that it can store its audio clips once
		/// they finish downloading.
		/// </summary>
		/// <param name="cluster"></param>
		private static void InitializeCluster(TTSAudioClipCluster cluster)
		{
			var speed = StringProcessor.GetSpeed(cluster.ID);
			if (!_ttsClipData.ContainsKey(speed))
			{
				_ttsClipData.Add(speed, new ClipDataContainer());
			}

			_ttsClipData[speed].AddClusterObject(cluster);
		}

		/// <summary>
		/// Stores the given <paramref name="audioClip"/> and makes it available for use.
		/// </summary>
		/// <param name="ID"></param>
		/// <param name="audioClip"></param>
		private static void AddClip(string ID, AudioClip audioClip)
		{
			var speed = StringProcessor.GetSpeed(ID);
			if (!_ttsClipData.ContainsKey(speed))
			{
				_ttsClipData.Add(speed, new ClipDataContainer());
			}

			_ttsClipData[speed].AddClip(ID, audioClip);
		}

		/// <summary>
		/// Manages single audio clips and clusters for a single speed setting.
		/// </summary>
		private class ClipDataContainer
		{
			/// <summary>
			/// Contains all the audio clips that were short enough to not need to be split.
			/// <br/><br/>
			/// They key is the base ID of the audio clip (doesn't contain accessibility markers (see 
			/// <see cref="StringProcessor.RemoveAccessibilityMarkers"/>).
			/// </summary>
			private Dictionary<string, BaseClipData<AudioClip>> _ttsClipData = new();

			/// <summary>
			/// Contains all the audio clips that were too long to be kept as a single clip, so they
			/// were split into multiple ones.
			/// <br/><br/>
			/// They key is the base ID of the audio clip (doesn't contain accessibility markers
			/// (see <see cref="StringProcessor.RemoveAccessibilityMarkers"/>).
			/// <br/><br/>
			/// WARNING: Any new <see cref="TTSAudioClipCluster"/> objects should be added whenever a 
			/// cluster is created (by using <see cref="InitializeCluster"/>), otherwise adding
			/// audio clips to clusters will not work (see <see cref="AddClusterComponent"/>).
			/// </summary>
			private Dictionary<string, BaseClipData<TTSAudioClipCluster>> _ttsClipDataClusters = new();

			/// <summary>
			/// Stores the given <paramref name="audioClip"/>.
			/// <br/><br/>
			/// The <paramref name="ID"/> must also have accessibility markers applied (see
			/// <see cref="StringProcessor.ApplyAccessibilityMarkers"/>).
			/// </summary>
			/// <param name="ID"></param>
			/// <param name="audioClip"></param>
			public void AddClip(string ID, AudioClip audioClip)
			{
				if (!ID.Contains('_'))
				{
					AddSingleClip(ID, audioClip);
				}
				else
				{
					AddClusterComponent(ID, audioClip);
				}
			}

			/// <summary>
			/// Stores the given <paramref name="cluster"/>, which will be used to contain
			/// cluster components that are added through <see cref="AddClusterComponent"/>.
			/// </summary>
			/// <param name="cluster"></param>
			public void AddClusterObject(TTSAudioClipCluster cluster)
			{
				var gender = StringProcessor.GetGender(cluster.ID);
				var baseID = StringProcessor.RemoveAccessibilityMarkers(cluster.ID);

				if (gender.HasValue)
				{
					if (!_ttsClipDataClusters.ContainsKey(baseID))
					{
						_ttsClipDataClusters[baseID] = new GenderedClipData<TTSAudioClipCluster>();
					}
					(_ttsClipDataClusters[baseID] as GenderedClipData<TTSAudioClipCluster>).Add(gender.Value, cluster);
				}
				else
				{
					_ttsClipDataClusters.Add(baseID, new ClipData<TTSAudioClipCluster>(cluster));
				}
			}

			/// <summary>
			/// Returns an audio clip for the given ID, if it has been processed and if
			/// the text that was provided for it hasn't been processed as a cluster.
			/// <br/><br/>
			/// The <paramref name="baseID"/> must NOT have accessibility markers applied (see
			/// <see cref="StringProcessor.RemoveAccessibilityMarkers"/>).
			/// <br/><br/>
			/// Check if a given ID is a cluster by using <see cref="IsCluster"/>.
			/// </summary>
			/// <param name="baseID"></param>
			/// <returns></returns>
			public AudioClip GetClip(string baseID, bool? isMale)
			{
				if (_ttsClipData.ContainsKey(baseID) && _ttsClipData[baseID].HasClipForGender(isMale))
				{
					return _ttsClipData[baseID].GetData(isMale);
				}

				// if it reaches this point, something went more or less wrong

				if (_ttsClipDataClusters.ContainsKey(baseID))
				{
					// there is a list of audio clips for this ID
					Debug.LogError("Clip with ID " + baseID + " was processed as a cluster.");
				}
				else
				{
					// either the clip wasn't downloaded yet or has never been received to be downloaded
					Debug.LogError("Clip with ID " + baseID + " hasn't been processed yet for the given sound settings.");
				}

				return null;
			}

			/// <summary>
			/// Returns a list of audio clips for the given ID, if it has been received to be 
			/// downloaded and if the text that was provided for it was too long to be processed 
			/// as a single audio clip.
			/// <br/><br/>
			/// The <paramref name="baseID"/> must NOT have accessibility markers applied (see
			/// <see cref="StringProcessor.RemoveAccessibilityMarkers"/>).
			/// <br/><br/>
			/// Check if a given ID is a cluster by using <see cref="IsCluster"/>.
			/// </summary>
			/// <param name="ID"></param>
			/// <returns></returns>
			public List<AudioClip> GetClipCluster(string baseID, bool? isMale)
			{
				var cluster = GetClusterObject(baseID, isMale);
				if (cluster != null)
				{
					return cluster.GetClipList();
				}

				// if it reaches this point, something went more or less wrong

				if (_ttsClipData.ContainsKey(baseID))
				{
					// there is a single audio clip for this ID
					Debug.LogError("Clip with ID " + baseID + " was not processed as a cluster.");
				}
				else
				{
					// either the clip wasn't downloaded yet or has never been received to be downloaded
					Debug.LogError("Clip with ID " + baseID + " hasn't been processed yet for the given sound settings.");
				}

				return null;
			}

			/// <summary>
			/// Returns a cluster for the given ID, if it has been received to be downloaded and if
			/// the text that was provided for it was too long to be processed as a single audio clip.
			/// <br/><br/>
			/// The <paramref name="baseID"/> must NOT have accessibility markers applied (see
			/// <see cref="StringProcessor.RemoveAccessibilityMarkers"/>).
			/// <br/><br/>
			/// Check if a given ID is a cluster by using <see cref="IsCluster"/>.
			/// </summary>
			/// <param name="baseID"></param>
			/// <param name="isMale"></param>
			/// <returns></returns>
			public TTSAudioClipCluster GetClusterObject(string baseID, bool? isMale)
			{
				if (_ttsClipDataClusters.ContainsKey(baseID) && _ttsClipDataClusters[baseID].HasClipForGender(isMale))
				{
					return _ttsClipDataClusters[baseID].GetData(isMale);
				}

				return null;
			}

			/// <summary>
			/// Returns true if the text that was provided for the given ID is long enough to have been
			/// processed as a cluster (see <see cref="GetClipCluster"/>).
			/// <br/><br/>
			/// The <paramref name="baseID"/> must NOT have accessibility markers applied (see
			/// <see cref="StringProcessor.RemoveAccessibilityMarkers"/>).
			/// </summary>
			/// <param name="baseID"></param>
			/// <returns></returns>
			public bool IsCluster(string baseID)
			{
				return _ttsClipDataClusters.ContainsKey(baseID);
			}

			/// <summary>
			/// Returns true if the clip with the given <paramref name="baseID"/> has been processed
			/// successfully and is available through <see cref="GetClip"/> or <see cref="GetClipCluster"/>.
			/// <br/><br/>
			/// The <paramref name="baseID"/> must NOT have accessibility markers applied (see
			/// <see cref="StringProcessor.RemoveAccessibilityMarkers"/>).
			/// </summary>
			/// <param name="baseID"></param>
			/// <returns></returns>
			public bool IsReady(string baseID, bool? isMale)
			{
				if (!IsCluster(baseID))
				{
					return _ttsClipData.ContainsKey(baseID) && _ttsClipData[baseID].HasClipForGender(isMale);
				}

				var cluster = _ttsClipDataClusters[baseID].HasClipForGender(isMale) ?
					_ttsClipDataClusters[baseID].GetData(isMale) : null;

				return (cluster?.IsComplete) ?? false;
			}

			/// <summary>
			/// Returns true if the cluster with the given <paramref name="baseID"/> has at least
			/// one <see cref="AudioClip"/> that has been downloaded (available through
			/// <see cref="GetClipCluster"/>).
			/// <br/><br/>
			/// The <paramref name="baseID"/> must NOT have accessibility markers applied (see
			/// <see cref="StringProcessor.RemoveAccessibilityMarkers"/>).
			/// </summary>
			/// <param name="baseID"></param>
			/// <param name="isMale"></param>
			/// <returns></returns>
			public bool IsClusterAvailable(string baseID, bool? isMale)
			{
				if (!IsCluster(baseID))
				{
					return false;
				}

				var cluster = _ttsClipDataClusters[baseID].HasClipForGender(isMale) ?
					_ttsClipDataClusters[baseID].GetData(isMale) : null;

				return cluster != null && cluster.ClipCount > 0;
			}

			private void AddSingleClip(string ID, AudioClip audioClip)
			{
				var gender = StringProcessor.GetGender(ID);
				var baseID = StringProcessor.RemoveAccessibilityMarkers(ID);

				if (gender.HasValue)
				{
					if (!_ttsClipData.ContainsKey(baseID))
					{
						_ttsClipData[baseID] = new GenderedClipData<AudioClip>();
					}
					(_ttsClipData[baseID] as GenderedClipData<AudioClip>).Add(gender.Value, audioClip);
				}
				else
				{
					_ttsClipData.Add(baseID, new ClipData<AudioClip>(audioClip));
				}
			}

			/// <summary>
			/// Adds the given <paramref name="audioClip"/> to its corresponding cluster.
			/// <br/><br/>
			/// WARNING: Make sure that <see cref="InitializeCluster"/> had been called
			/// beforehand in order to avoid erors (see the warning on 
			/// <see cref="_ttsClipDataClusters"/> for more details).
			/// </summary>
			/// <param name="ID"></param>
			/// <param name="audioClip"></param>
			private void AddClusterComponent(string ID, AudioClip audioClip)
			{
				var gender = StringProcessor.GetGender(ID);
				var baseID = StringProcessor.RemoveAccessibilityMarkers(ID);

				var clusterBaseID = baseID.Split('_')[0]; // the ID of a cluster component is [clusterID]_[index]

				// the cluster should already be present in the dictionary. but if there's an error,
				// the cluster object wasn't added to the list when it was created. more details in
				// the summary of this method
				var cluster = gender.HasValue ?
					(_ttsClipDataClusters[clusterBaseID] as GenderedClipData<TTSAudioClipCluster>).GetData(gender.Value) :
					(_ttsClipDataClusters[clusterBaseID] as ClipData<TTSAudioClipCluster>).GetData();

				cluster.AddClip(ID, audioClip);
			}

			/// <summary>
			/// Handles single audio clips or a cluster that has gender settings.
			/// </summary>
			/// <typeparam name="Data"></typeparam>
			private class GenderedClipData<Data> : BaseClipData<Data> where Data : class
			{
				private Dictionary<bool, Data> _clipCache = new();

				public void Add(bool gender, Data data)
				{
					_clipCache.Add(gender, data);
				}

				public override Data GetData(bool? gender) => _clipCache[gender.Value];

				public override bool HasClipForGender(bool? isMale) =>
					_clipCache.ContainsKey(isMale.Value);
			}

			/// <summary>
			/// Handles single audio clips or a cluster that does not have gender settings.
			/// </summary>
			private class ClipData<Data> : BaseClipData<Data> where Data : class
			{
				private Data _clipCache;

				public ClipData(Data data)
				{
					_clipCache = data;
				}

				public override Data GetData(bool? gender = null) => _clipCache;

				public override bool HasClipForGender(bool? _) => true;
			}

			/// <summary>
			/// Used in order to store audio clips that correspond to a single ID (for one speed setting) 
			/// that might or might not require gender settings.
			/// The <see cref="Data"/> type should be either a single <see cref="AudioClip"/> 
			/// (if it's a single clip) or a list of audio clips (if it's a cluster).
			/// </summary>
			private abstract class BaseClipData<Data> where Data : class
			{
				/// <summary>
				/// Returns the audio clip or list of audio clips that match the current gender settings
				/// (if it's the case).
				/// </summary>
				public abstract Data GetData(bool? gender);

				/// <summary>
				/// Returns <i>true</i> if an audio clip for the given gender is stored (if it's the case).
				/// </summary>
				/// <param name="isMale"></param>
				/// <returns></returns>
				public abstract bool HasClipForGender(bool? isMale);
			}
		}

		/// <summary>
		/// Handles downloading TTS audio files.
		/// </summary>
		static class TTSDownloader
		{
			/// <summary>
			/// The amount of time that will be waited for when receiving a HTTP 429 error
			/// (too many requests), in order to decrease our request rate and decrease the
			/// chances of getting another one.
			/// </summary>
			private const float WAITING_TIME_AFTER_429 = 2f;

			public static Action<string, AudioClip> OnClipDownloaded;
			/// <summary>
			/// If not null, the audio clip or cluster with this ID will be downloaded as 
			/// soon as possible.
			/// </summary>
			public static string HighPriorityRequestId
			{
				get => _highPriorityRequestId;
				set
				{
					var containsId = !string.IsNullOrEmpty(value);
					_isAwaitingHPRDownload = false;
					if (!containsId || IsReady(value, true))
					{
						// if the given string is null or the ID has been processed, there is nothing to be done
						_highPriorityRequestId = null;
						return;
					}

					if (!StringProcessor.HasAccessibilityMarkers(value))
					{
						// the ID should have accessibility markers applied
						Debug.LogError($"Attempted to set the ID {value} as a high priority request, " +
							"but it does not have accessibility markers applied. Use " +
							$"{nameof(StringProcessor.ApplyAccessibilityMarkers)} first.");
						_highPriorityRequestId = null;
						return;
					}

					_highPriorityRequestId = value;
				}
			}

			/// <summary>
			/// The amount of time that is waited for before sending another
			/// request to the server, in order to avoid HTTP 429 (too many requests).
			/// </summary>
			private static float _requestSendDelay = 1f;
			/// <summary>
			/// If <see cref="_requestSendDelay"/> is too small, increase it by this amount.
			/// </summary>
			private static float _delayIncreaseAmount = 0.1f;
			/// <summary>
			/// The amount of requests that have been sent to the server but haven't received a response.
			/// </summary>
			private static int _awaitingProcessingCount = 0;
			/// <summary>
			/// If true, it will prevent sending more requests (from the queue) to the server and
			/// other processes until all requests have received a response (meaning that
			/// <see cref="_awaitingProcessingCount"/> reaches 0).
			/// </summary>
			private static bool _shouldAwaitCompletion = false;

			/// <summary>
			/// Contains all the clips that have become available through <see cref="OnDownloadFinished"/>,
			/// but are waiting for their <see cref="TTSClipData.clipStream"/> to finish writing the
			/// audio data so that it can be extracted as <see cref="AudioClip"/> and cached
			/// (see <see cref="ExtractAudioClip"/>).
			/// <br/><br/>
			/// Is it possible that, if the scene unloads, some clips might be deleted
			/// before the extraction can take place, so those clips need to be re-added to the 
			/// download queue.
			/// </summary>
			private static List<TTSClipData> _awaitingAudioExtraction = new List<TTSClipData>();
			/// <summary>
			/// Determines the order in which the clips are being downloaded.
			/// </summary>
			private static ITTSDownloadHelper _downloadHelper;
			private static Coroutine _requestsProcessingCoroutine;

			// high priority
			/// <summary>
			/// If not null, the audio clip or cluster with this ID will be downloaded first.
			/// <br/><br/>
			/// WARNING: this variable should only be changed through <see cref="HighPriorityRequestId"/>.
			/// </summary>
			private static string _highPriorityRequestId;
			/// <summary>
			/// Is <i>true</i> if the <see cref="_highPriorityRequestId"/> has been processed and is
			/// awaiting the response from the server.
			/// </summary>
			private static bool _isAwaitingHPRDownload;

			public static void Initialize(ITTSDownloadHelper downloadHelper)
			{
				_downloadHelper = downloadHelper;
			}

			/// <summary>
			/// Adds a request to the queue, to be downloaded.
			/// <br/><br/>
			/// WARNING: The parameter <paramref name="bypassSizeCheck"/> should be <i>true</i> only
			/// if the <paramref name="request"/> text (not including appended and preappended) is
			/// sure to not exceed <see cref="StringProcessor.MAX_STRING_SIZE"/>, otherwise it won't
			/// be added to the queue.
			/// </summary>
			/// <param name="request"></param>
			/// <param name="bypassSizeCheck"></param>
			public static void AddToWaitingList(TTSRequestData request, bool bypassSizeCheck = false)
			{
				if (!bypassSizeCheck && StringProcessor.IsCluster(request.Text))
				{
					Debug.LogError("The text with ID " + request.ID + " cannot be processed as a single clip.");
					return;
				}

				if (request.ID != null)
				{
					_downloadHelper.AddRequestToQueue(request);
				}

				StartDownloadCoroutine(true);
			}

			/// <summary>
			/// Adds a cluster to the queue, to be downloaded.
			/// </summary>
			/// <param name="ID"></param>
			/// <param name="clusterComponents"></param>
			public static void AddClusterToWaitingList(string ID, List<(string ID, string text)> clusterComponents, string speaker)
			{
				InitializeCluster(new TTSAudioClipCluster(ID, clusterComponents.Count));

				foreach (var component in clusterComponents)
				{
					var request = new TTSRequestData { ID = component.ID, Text = component.text, Speaker = speaker };

					TTSVoiceManager.ApplyVoiceStyle(ref request);
					AddToWaitingList(request);
				}
			}

			/// <summary>
			/// Begins the <see cref="ProcessRequests"/> coroutine if one isn't already in progress, and
			/// if there are requests to be processed. If the <paramref name="ignoreRequestCount"/>
			/// parameter is <i>true</i>, then the number of requests will be ignored.
			/// </summary>
			/// <param name="ignoreRequestCount"></param>
			public static void StartDownloadCoroutine(bool ignoreRequestCount = false)
			{
				if (_requestsProcessingCoroutine != null ||
					(!ignoreRequestCount && _downloadHelper.RequestCount == 0)) return;

				_requestsProcessingCoroutine = Instance.StartCoroutine(ProcessRequests());
			}

			/// <summary>
			/// Force-ends the <see cref="ProcessRequests"/> coroutine. Should be called when
			/// exitting a scene in order to properly handle the download interruption upon the
			/// destruction of the TTS game object.
			/// </summary>
			public static void StopDownloadCoroutine()
			{
				if (_requestsProcessingCoroutine == null) return;

				Instance.StopCoroutine(_requestsProcessingCoroutine);
				_requestsProcessingCoroutine = null;
			}

			/// <summary>
			/// Re-adds the <see cref="TTSClipData"/> objects that have not completed the audio extraction
			/// process during the previous scene back to the download queue (see the summary of 
			/// <see cref="_awaitingAudioExtraction"/>).
			/// <br/><br/>
			/// WARNING: Should only be called once after loading a new scene.
			/// </summary>
			public static void ReprocessAwaitingAudioExtractionClips()
			{
				foreach (var clipData in _awaitingAudioExtraction)
				{
					AddToWaitingList(new TTSRequestData
					{
						ID = clipData.clipID,
						Text = clipData.textToSpeak,
						VoiceSettings = clipData.voiceSettings
					}, true);
				}

				_awaitingAudioExtraction.Clear();
			}

			/// <summary>
			/// Returns the audio clip extracted from <see cref="TTSClipData.clipStream"/>
			/// while also removing the silence at the beginning and end.
			/// </summary>
			/// <param name="clipData"></param>
			public static AudioClip ExtractAudioClip(TTSClipData clipData)
			{
				float[] samples = null;
				int length = 0;

				// get the original sample array

				if (clipData.clipStream is IAudioClipProvider uacs)
				{
					var clip = uacs.Clip;
					if (clip == null) return null;

					length = clip.samples;
					samples = new float[length];

					if (!clip.GetData(samples, 0)) return null;
				}
				else if (clipData.clipStream is RawAudioClipStream rawAudioClipStream)
				{
					samples = rawAudioClipStream.SampleBuffer;
					length = rawAudioClipStream.TotalSamples;
				}

				// trim the silence on both ends

				int startIndex = 0;
				int endIndex = length - 1;

				// trim the silence at the beginning
				while (startIndex < length - 1 && samples[startIndex] == 0)
				{
					startIndex++;
				}

				// trim the silence at the end
				while (endIndex > startIndex && samples[endIndex] == 0)
				{
					endIndex--;
				}

				// copy the resulting middle section into a new array

				var newLength = length - startIndex - (length - endIndex + 1);
				var newSamples = new float[newLength];
				Array.Copy(samples, startIndex, newSamples, 0, newLength);

				var newClip = AudioClip.Create(clipData.clipID, newLength,
				WitConstants.ENDPOINT_TTS_CHANNELS, WitConstants.ENDPOINT_TTS_SAMPLE_RATE, false);
				newClip.SetData(samples, 0);

				// return the resulting audio clip

				_awaitingAudioExtraction.Remove(clipData);
				return newClip;
			}

			/// <summary>
			/// Called when a <see cref="TTSClipData"/> becomes available (the actual audio isn't available
			/// right at that moment, as it still needs to be written).
			/// </summary>
			/// <param name="clipData"></param>
			/// <param name="error"></param>
			private static void OnDownloadFinished(TTSClipData clipData, string error)
			{
				_awaitingProcessingCount--;

				if (string.IsNullOrEmpty(error))
				{
					_awaitingAudioExtraction.Add(clipData);

					// wait until the audio stream data is fully written, then process it
					clipData.clipStream.OnStreamComplete += (_) => OnAudioStreamComplete(clipData);

					return;
				}

				// if it has reached this point, there has been an error

				if (error.Contains("429"))
				{
					// too many requests per second
					// re-add the text to queue
					AddToWaitingList(new TTSRequestData
					{
						ID = clipData.clipID,
						Text = clipData.textToSpeak,
						VoiceSettings = clipData.voiceSettings
					}, true);

					if (_shouldAwaitCompletion) return;

					//increase the waiting time between requests
					_requestSendDelay += _delayIncreaseAmount;

					// lock everything and wait until all requests have been cleared before
					// starting to send more again
					_shouldAwaitCompletion = true;
				}
				else
				{
					// HTTP 400: bad request
					// failed to download for some reason. potential causes (known so far):
					// - text is too long (somehow didn't go through StringProcessor.Split() before
					// being sent to the server)
					// - the formatting is wrong when applying voices and styles
					// (see TTSSpeakerEffectSelect.RefreshSsml())
					// - the plugin is not up to date

					var errorString = $"Download failed for clip with ID {clipData.clipID}.";

					// check if the clip is a high priority request (single clip or part of cluster),
					// and reset it so that it doesn't get sent to the server over and over again
					if (HighPriorityRequestId != null)
					{
						var (baseID, settings) = GetSettingsForID(HighPriorityRequestId, true, null);
						var isPartOfHPCluster = IsCluster(HighPriorityRequestId) &&
							_ttsClipData[settings.Speed].GetClusterObject(baseID, settings.IsMale)
							.IsPartOfCluster(clipData.clipID);

						if (clipData.clipID == HighPriorityRequestId || isPartOfHPCluster)
						{
							HighPriorityRequestId = null;
							errorString += " It is also a high priority request, so the variable will be reset to null.";
						}
					}

					Debug.LogError(errorString);
				}
			}

			/// <summary>
			/// Called when the <see cref="TTSClipData.clipStream"/> has finished writing all
			/// audio data.
			/// </summary>
			/// <param name="clipData"></param>
			private static void OnAudioStreamComplete(TTSClipData clipData)
			{
				clipData.clipStream.OnStreamComplete = null;

				var audioclip = ExtractAudioClip(clipData);
				OnClipDownloaded?.Invoke(clipData.clipID, audioclip);

				// check if it's a high priority request, and reset the variables if needed

				if (HighPriorityRequestId == null) return;

				if (!clipData.clipID.Contains('_'))
				{
					// if the clip is not part of a cluster, then its ID would match the HPR
					if (clipData.clipID == HighPriorityRequestId)
					{
						HighPriorityRequestId = null;
					}
				}
				else
				{
					// if the clip is a cluster component, we need to check if the HP cluster
					// is complete
					if (IsCluster(HighPriorityRequestId))
					{
						var (baseID, settings) = GetSettingsForID(HighPriorityRequestId, true, null);
						var highPriorityCluster = _ttsClipData[settings.Speed].GetClusterObject(baseID, settings.IsMale);

						if (highPriorityCluster.IsComplete)
						{
							HighPriorityRequestId = null;
						}
					}
				}
			}

			/// <summary>
			/// Progressively sends requests to the server, as long as there are any in the queue.
			/// </summary>
			/// <returns></returns>
			private static IEnumerator ProcessRequests()
			{
				yield return null;

				while ((!string.IsNullOrEmpty(HighPriorityRequestId) && !_isAwaitingHPRDownload)
					|| _downloadHelper.RequestCount > 0)
				{
					if (_shouldAwaitCompletion) // is true only after receiving a HTTP 429 error
					{
						// stop sending any more requests, and wait until all pending ones (not enqueued)
						// have received a response from the server
						yield return AwaitProcessingCurrentRequests();

						_shouldAwaitCompletion = false; // reset and allow requests to be sent again

						// wait for a bit longer before starting to send requests again, in order
						// to decrease our current RPS rate and have less chances of getting another 429
						yield return new WaitForSeconds(WAITING_TIME_AFTER_429);
					}

					TTSRequestData request;

					// check if there is anything that needs to be downloaded immediately, else the
					// regular downloading order can be resumed
					if (!string.IsNullOrEmpty(HighPriorityRequestId) && !_isAwaitingHPRDownload)
					{
						var (baseID, settings) = GetSettingsForID(HighPriorityRequestId, true, null);

						var requestId = IsCluster(HighPriorityRequestId) ?
							_ttsClipData[settings.Speed].GetClusterObject(baseID, settings.IsMale).GetFirstMissingIndexID() :
							HighPriorityRequestId;

						request = _downloadHelper.GetRequest(requestId);
						_isAwaitingHPRDownload = true;
					}
					else
					{
						request = _downloadHelper.GetHighestPriorityRequest();
					}

					// send a request

					if (request != null)
					{
						var voiceSettings = TTSVoiceManager.ApplySpeed(request.VoiceSettings, StringProcessor.GetSpeed(request.ID));
						Instance._ttsService.Load(request.FullText, request.ID, voiceSettings,
						Instance._ttsDiskCache.DiskCacheDefaultSettings,
						OnDownloadFinished);

						_awaitingProcessingCount++;

						yield return new WaitForSeconds(_requestSendDelay);
					}
					else
					{
						Debug.LogError("Current highest priority request is null.");
					}
				}

				_requestsProcessingCoroutine = null;
			}

			/// <summary>
			/// Waits until all current requests (not enqueued) have received a response from the server.
			/// </summary>
			/// <returns></returns>
			private static IEnumerator AwaitProcessingCurrentRequests()
			{
				if (_awaitingProcessingCount <= 0) // nothing to wait for
				{
					yield return 0;
				}
				else
				{
					while (_awaitingProcessingCount > 0)
					{
						yield return 0;
					}
				}
			}
		}

		/// <summary>
		/// Support class that prepares a given string to be downloaded.
		/// </summary>
		public static class StringProcessor
		{
			private const string GENDER_MARKER = "isMale";
			private const string SPEED_MARKER = "speed";
			/// <summary>
			/// The maximum length of a string that can be processed by the server. Anything beyond
			/// this size will have to be split.
			/// </summary>
			private const int MAX_STRING_SIZE = 200;
			/// <summary>
			/// Used when checking if a given string exceeds <see cref="MAX_STRING_SIZE"/>. 
			/// <see cref="string.Length"/> counts each of them as 1 single character, but they count as 2
			/// when sent to the server.
			/// </summary>
			private const string ESCAPE_CHARACTERS = "\'\"\n\\";
			/// <summary>
			/// Contains all the characters that should be removed if found at the beginning 
			/// of a string.
			/// </summary>
			private const string OMITTED_BEGINNING_CHARACTERS = "-. ";
			/// <summary>
			/// Contains all the punctuation characters that can end a sentence. It's used in
			/// order to split a large text into a cluster (see
			/// <see cref="ProcessClusterText"/>).
			/// </summary>
			private const string FINAL_PUNCTUATION_CHARACTERS = ".?!\n;";
			/// <summary>
			/// Contains all the punctuation characters that can be used in the middle of a sentence
			/// (without ending it). It's used in order to split a large text into a cluster,
			/// as a last resort if splitting by <see cref="FINAL_PUNCTUATION_CHARACTERS"/> didn't
			/// break it down into parts that are short enough.
			/// </summary>
			private const string MEDIAN_PUNCTUATION_CHARACTERS = ",:;";
			private static readonly Dictionary<int, string> SPLITTING_CHARACTERS = new()
			{
				{ 1, FINAL_PUNCTUATION_CHARACTERS },
				{ 2, MEDIAN_PUNCTUATION_CHARACTERS },
				{ 3, " " } // worst case scenario
			};

			/// <summary>
			/// Returns <i>true</i> if the given string is larger than <see cref="MAX_STRING_SIZE"/>.
			/// </summary>
			/// <param name="text"></param>
			/// <returns></returns>
			public static bool IsCluster(string text)
			{
				return text.Length > MAX_STRING_SIZE;
			}

			/// <summary>
			/// Deletes any characters that might be mistakenly read out loud.
			/// </summary>
			/// <param name="text"></param>
			public static string RemoveOmittedCharacters(string text)
			{
				// 1. remove the characters at the beginning of the text

				var lastOmmitedCharacterIndex = 0;

				// count how many characters need to be removed from the beginning of a string
				while (lastOmmitedCharacterIndex < text.Length &&
					OMITTED_BEGINNING_CHARACTERS.Contains(text[lastOmmitedCharacterIndex]))
				{
					lastOmmitedCharacterIndex++;
				}

				// if any were found, remove them
				if (lastOmmitedCharacterIndex > 0)
				{
					text = text.Remove(0, lastOmmitedCharacterIndex);
				}

				// 2. turn "\\n" into ' '

				var slashIndexes = new List<int>();

				// "\\n" is written as 2 characters: '\\' and 'n'. we'll look for the
				// relevant '\\' characters and keep track of their positons in the string
				for (int i = 0; i < text.Length; i++)
				{
					if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == 'n')
					{
						var prev = string.Empty;
						for (int j = 6; j > -1; j--)
						{
							if (i >= j)
							{
								prev += text[i - j];
							}
						}

						slashIndexes.Add(i);
					}
				}

				// if nothing was found, then no need to change anything
				if (slashIndexes.Count == 0) return text;

				var stringBuilder = new StringBuilder();

				for (int i = 0; i <= slashIndexes.Count; i++)
				{
					var startIndex = i == 0 ? 0 : slashIndexes[i - 1] + 2; // at index[i-1] is the previous '\\' character, so +1 would be 'n', and +2 would be the beginning of the current text section
					var endIndex = i < slashIndexes.Count ? slashIndexes[i] - 1  // -1 because we don't want to include it the current '\\' character
						: text.Length - 1; // this will only happen when appending the last section of the text, for which there is no index in the slashIndexes list

					if (startIndex < endIndex)
					{
						stringBuilder.Append(text.Substring(startIndex, endIndex - startIndex + 1));
						stringBuilder.Append(' '); // replace "\\n" with ' '
					}
				}

				var newText = stringBuilder.ToString();
				newText = newText.Trim();
				return newText;
			}

			/// <summary>
			/// Removes the formatting from a given string.
			/// </summary>
			/// <param name="text"></param>
			/// <returns></returns>
			public static string RemoveFormatting(string text)
			{
				return Regex.Replace(text, "<[^<>]*>", " ");
			}

			/// <summary>
			/// Returns <i>true</i> if the given <see cref="text"/> contains actual words that can
			/// be spoken.
			/// </summary>
			/// <param name="text"></param>
			/// <returns></returns>
			public static bool ContainsSpeech(string text)
			{
				var match = Regex.Match(text, "[A-z,a-z,0-9]+");

				return match?.Success ?? false;
			}

			/// <summary>
			/// </summary>
			/// <param name="ID"></param>
			/// <returns><i>true</i> if the given <paramref name="ID"/> has been through
			/// <see cref="ApplyAccessibilityMarkers"/></returns>
			public static bool HasAccessibilityMarkers(string ID)
			{
				var match = Regex.Match(ID, $"\\[.+\\]");
				return match.Success;
			}

			/// <summary>
			/// Appends the given <paramref name="ID"/> with a string that contains the corresponding
			/// sound settings.
			/// </summary>
			/// <param name="ID"></param>
			/// <param name="speed"></param>
			/// <param name="isMale"></param>
			/// <returns></returns>
			public static string ApplyAccessibilityMarkers(string ID, int speed, bool? isMale = null)
			{
				var appendedText = $"[{SPEED_MARKER}={speed}";
				if (isMale.HasValue)
				{
					appendedText += $";{GENDER_MARKER}={isMale.Value}";
				}

				appendedText += ']';

				return appendedText + ID;
			}

			/// <summary>
			/// Removes the changes that were made in <see cref="ApplyAccessibilityMarkers"/>.
			/// </summary>
			/// <param name="ID"></param>
			/// <returns></returns>
			public static string RemoveAccessibilityMarkers(string ID)
			{
				return ID.Remove(0, ID.IndexOf(']') + 1);
			}

			/// <summary>
			/// Extracts the gender value from the given <paramref name="ID"/>, which had been applied
			/// through <see cref="ApplyAccessibilityMarkers"/>.
			/// </summary>
			/// <param name="ID"></param>
			/// <returns></returns>
			public static bool? GetGender(string ID)
			{
				var match = Regex.Match(ID, $"{GENDER_MARKER}=[a-zA-Z]+");

				if (!match.Success) return null;

				try
				{
					var result = match.Captures[0].Value.Replace($"{GENDER_MARKER}=", string.Empty);
					return bool.Parse(result);
				}
				catch
				{
					Debug.LogError("Couldn't extract the gender value from ID " + ID);
					return null;
				}
			}

			/// <summary>
			/// Extracts the speed value from the given <paramref name="ID"/>, which had been applied
			/// through <see cref="ApplyAccessibilityMarkers"/>.
			/// </summary>
			/// <param name="ID"></param>
			/// <returns></returns>
			public static int GetSpeed(string ID)
			{
				var match = Regex.Match(ID, $"{SPEED_MARKER}=[0-9|.]+");

				if (!match.Success) return 1;

				try
				{
					var result = match.Captures[0].Value.Replace($"{SPEED_MARKER}=", string.Empty);
					return int.Parse(result);
				}
				catch
				{
					Debug.LogError("Couldn't extract the speed value from ID " + ID);
					return 1;
				}
			}

			/// <summary>
			/// Returns a <see cref="BaseSoundSettingsModel"/> containing the parameters that have been applied
			/// through <see cref="ApplyAccessibilityMarkers"/>.
			/// </summary>
			/// <param name="ID"></param>
			/// <returns></returns>
			public static BaseSoundSettingsModel ExtractAccessibilitySettings(string ID)
			{
				return new BaseSoundSettingsModel
				{
					IsMale = GetGender(ID) ?? false,
					Speed = GetSpeed(ID)
				};
			}

			/// <summary>
			/// Splits a large string into shorter strings that can be downloaded, and returns them
			/// as a list with their unique IDs.
			/// </summary>
			/// <param name="ID"></param>
			/// <param name="text"></param>
			/// <returns></returns>
			public static List<(string ID, string text)> GetCluster(string ID, string text)
			{
				var cluster = new List<(string ID, string Text)>();
				ProcessClusterText(ID, text, cluster, 1);
				return cluster;
			}

			/// <summary>
			/// Breaks down the given <paramref name="text"/> into smaller strings that don't exceed
			/// <see cref="MAX_STRING_SIZE"/>.
			/// </summary>
			/// <param name="id">the ID of the main text <see cref="PrepareClip"/></param>
			/// <param name="text">the text that is to be split into smaller parts</param>
			/// <param name="splittingLevel">determines what characters are used in order to split
			/// the given text; is increased during recursion (see <see cref="SPLITTING_CHARACTERS"/>)</param>
			private static void ProcessClusterText(string id, string text, List<(string ID, string Text)> cluster, int splittingLevel)
			{
				// if there are no more splitting levels available, then most likely the given text
				// doesn't have enough puctuation
				if (splittingLevel > SPLITTING_CHARACTERS.Count)
				{
					Debug.LogError("Text with ID " + id + " couldn't be split.");
					return;
				}

				var splitText = Split(text, SPLITTING_CHARACTERS[splittingLevel]);

				var currentText = string.Empty;

				for (int i = 0; i < splitText.Count; i++)
				{
					// if splitText[i] is too large, it needs to be re-processed with the next splitting level
					if (splitText[i].Length > MAX_STRING_SIZE)
					{
						// add the current text to the cluster first, because it comes before splitText[i]
						AddToCluster();

						// send the current split to be re-processed
						ProcessClusterText(id, splitText[i], cluster, splittingLevel + 1);
						continue;
					}

					// try to increase the size of the current text as much as possible before
					// sending it to the server
					if (currentText.Length + splitText[i].Length < MAX_STRING_SIZE)
					{
						currentText += splitText[i] + ' ';
					}
					else
					{
						AddToCluster();
						currentText = splitText[i] + ' ';
					}
				}

				// the for loop doesn't add very last text to the cluster, so it has to be called here
				AddToCluster();

				// add the said text to the cluster list, with a unique ID based on the ID on the
				// main text and its order in said text
				void AddToCluster()
				{
					if (string.IsNullOrEmpty(currentText)) return;

					currentText = currentText.Trim();
					var textId = $"{id}_{cluster.Count + 1}";
					cluster.Add((textId, currentText));

					currentText = string.Empty;
				}
			}

			/// <summary>
			/// Does what <see cref="string.Split(char[])"/> would do, but keeps the delimiters and removes
			/// parts that don't have text.
			/// </summary>
			/// <param name="text"></param>
			/// <param name="delimiters"></param>
			/// <returns></returns>
			private static List<string> Split(string text, string delimiters)
			{
				var result = new List<string>();

				var previousIndex = 0;

				for (int i = 1; i < text.Length; i++)
				{
					if (i == previousIndex) continue;

					if (i == text.Length - 1 || delimiters.Contains(text[i]))
					{
						var substring = text.Substring(previousIndex, i - previousIndex + 1);
						substring = RemoveOmittedCharacters(substring);

						if (substring.Length > 1 && ContainsSpeech(substring)) // if it's just 1 character, most likely it's a delimiter
						{
							result.Add(substring);
						}

						previousIndex = i + 1;
					}
				}

				return result;
			}
		}

		/// <summary>
		/// Support class that handles the use of custom TTS voices and effects.
		/// </summary>
		static class TTSVoiceManager
		{
			/// <summary>
			/// The text that will be added at the beginning of a string in order to apply a voice style setting
			/// (see <see cref="ApplyVoiceStyle"/>). The # character should be replaced with the actual
			/// name of the style.
			/// </summary>
			private const string STYLE_PREAPPENDED_TEXT = "<speak><sfx character=\"#\"> ";
			/// <summary>
			/// The text that will be added at the end of a string in order to apply a voice style setting
			/// (see <see cref="ApplyVoiceStyle"/>).
			/// </summary>
			private const string STYLE_APPENDED_TEXT = " </sfx></speak>";

			private static Dictionary<TTSVoiceModel, TTSWitVoiceSettings> _ttsVoiceSettings;

			public static void Initialize(string voicesPath)
			{
				if (voicesPath == null) return;

				// load the voice data for each known speaker

				var voiceModels = JsonSerializerUtils.LoadJson<GenericSerializable<List<TTSVoiceModel>>>(voicesPath)?.Data;
				_ttsVoiceSettings = new();
				var voicePresets = Instance._ttsWit.VoiceProvider.PresetVoiceSettings;

				foreach (var configuration in voiceModels)
				{
					var voiceSettings = voicePresets.Where(vs => vs.SettingsId.Contains(configuration.Voice)).
								FirstOrDefault();
					if (voiceSettings != null && voiceSettings is TTSWitVoiceSettings ttsVS)
					{
						_ttsVoiceSettings.Add(configuration, ttsVS);
					}
				}
			}

			/// <summary>
			/// Returns the voice preset that corresponds to the given <paramref name="character"/>,
			/// usually determined by <see cref="TTSRequestData.Speaker"/> and <see cref="TTSVoiceModel.Voice"/>.
			/// </summary>
			/// <param name="character"></param>
			/// <returns></returns>
			public static TTSVoiceSettings GetVoiceSettings(string character, bool? isMale)
			{
				if (character == null) return Instance._ttsWit.VoiceDefaultSettings;

				if (isMale.HasValue)
				{
					character += '_' + (isMale.Value ? "male" : "female");
				}

				foreach (var (voice, settings) in _ttsVoiceSettings)
				{
					if (voice.Characters.Contains(character))
					{
						return settings;
					}
				}

				return Instance._ttsWit.VoiceDefaultSettings;
			}

			public static TTSVoiceSettings ApplySpeed(TTSVoiceSettings voiceSettings, int speed)
			{
				(voiceSettings as TTSWitVoiceSettings).speed = speed;
				return voiceSettings;
			}

			/// <summary>
			/// Sets the <see cref="TTSRequestData.PreappendedText"/> and <see cref="TTSRequestData.AppendedText"/>
			/// of the request in order to incorporate the voice style of the <see cref="TTSRequestData.Speaker"/>
			/// (see <see cref="TTSVoiceModel.Style"/>).
			/// </summary>
			/// <param name="request"></param>
			public static void ApplyVoiceStyle(ref TTSRequestData request)
			{
				var style = string.Empty;

				foreach (var (voice, _) in _ttsVoiceSettings)
				{
					if (voice.Characters.Contains(request.Speaker))
					{
						style = voice.Style;
						break;
					}
				}

				if (string.IsNullOrEmpty(style) || style == "default")
				{
					return;
				}

				request.PreappendedText = STYLE_PREAPPENDED_TEXT.Replace("#", style);
				request.AppendedText = STYLE_APPENDED_TEXT;
			}
		}

		/// <summary>
		/// Stores audio clips for a text that exceeds the maximum size and had to be split into 
		/// multiple parts.
		/// </summary>
		public class TTSAudioClipCluster
		{
			public readonly string ID;
			public bool IsComplete => ExpectedClipCount == _clipData.Count;
			public int ClipCount => _clipData.Count;
			public readonly int ExpectedClipCount;

			private SortedList<int, AudioClip> _clipData = new();

			public TTSAudioClipCluster(string id, int expectedClipcount)
			{
				ID = id;
				ExpectedClipCount = expectedClipcount;
			}

			/// <summary>
			/// </summary>
			/// <param name="clipID"></param>
			/// <returns><i>true</i> if the clip with the given <paramref name="clipID"/> is part of
			/// this cluster</returns>
			public bool IsPartOfCluster(string clipID)
			{
				if (!clipID.StartsWith(ID)) return false;

				// if the clipID starts with the ID of this cluster, it doesn't prove that
				// the clip is part of this cluster, so there still needs to be an additional check

				try
				{
					ExtractClipIndex(clipID);
					return true;
				}
				catch
				{
					// if this part fails, then the clip belongs to a different cluster
					return false;
				}
			}

			public void AddClip(string ID, AudioClip audioClip)
			{
				if (!IsPartOfCluster(ID))
				{
					Debug.LogError("The clip with ID " + ID + " doesn't belong to this clip cluster." +
						"Use " + nameof(IsPartOfCluster) + " first, in order to check the compatibility.");
					return;
				}

				var clipIndex = ExtractClipIndex(ID);
				_clipData.Add(clipIndex, audioClip);
			}

			public List<AudioClip> GetClipList()
			{
				return _clipData.Values.ToList();
			}

			/// <summary>
			/// Returns the ID of the first clip that is missing from the cluster.
			/// </summary>
			/// <returns></returns>
			public string GetFirstMissingIndexID()
			{
				if (IsComplete)
				{
					Debug.LogError($"Attempted to get the missing index of cluster {ID} that has " +
						$"already been completed. Use {nameof(IsComplete)} in order to check the state " +
						$"of the cluster.");
					return null;
				}

				for (int i = 1; i < ExpectedClipCount + 1; i++)
				{
					if (!_clipData.ContainsKey(i))
					{
						return $"{ID}_{i}";
					}
				}

				return null; // it shouldn't reach this point, but just to be safe
			}

			/// <summary>
			/// </summary>
			/// <param name="clipID"></param>
			/// <returns>the index of a clip in the cluster based on its <paramref name="clipID"/></returns>
			private int ExtractClipIndex(string clipID)
			{
				// the clipData id format is "{clusterId}_{index}"
				// get the index by removing the cluster ID (which is the ID member of this class)
				// and the underscore
				return int.Parse(clipID.Remove(0, ID.Length + 1));
			}
		}

		/// <summary>
		/// Contains the data that is necessary in order to convert text to speech.
		/// </summary>
		public class TTSRequestData
		{
			public string ID { get; set; }
			public string Text { get; set; }
			/// <summary>
			/// The text that will be added before <see cref="Text"/>, used for voice settings
			/// (see <see cref="TTSVoiceManager.ApplyVoiceStyle"/>).
			/// </summary>
			public string PreappendedText { get; set; }
			/// <summary>
			/// The text that will be added at the end of <see cref="Text"/>, used for voice settings
			/// (see <see cref="TTSVoiceManager.ApplyVoiceStyle"/>).
			/// </summary>
			public string AppendedText { get; set; }
			public string Speaker { get; set; }
			public TTSVoiceSettings VoiceSettings
			{
				get
				{
					if (_voiceSettings == null)
					{
						_voiceSettings = TTSVoiceManager.GetVoiceSettings(Speaker, StringProcessor.GetGender(ID));
					}

					return _voiceSettings;
				}
				set => _voiceSettings = value;
			}
			/// <summary>
			/// The text with the appended and preappended parts added to it.
			/// </summary>
			public string FullText => PreappendedText + Text + AppendedText;

			private TTSVoiceSettings _voiceSettings;
		}
	}
}
