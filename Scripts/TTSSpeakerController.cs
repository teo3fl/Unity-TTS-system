using System;
using System.Collections;
using System.Linq;
using T3FL.TTS.Settings;
using T3FL.TTS.Utils;
using UnityEngine;

namespace T3FL.TTS
{
	/// <summary>
	/// Handles the audio playback for objects that have TTS functionality (see
	/// <see cref="ITTSInputSource"/>).
	/// </summary>
	[RequireComponent(typeof(AudioSource))]
	public class TTSSpeakerController : MonoBehaviour
	{
		/// <summary>
		/// How often it checks if the audio source is playing (see <see cref="PlayAudio"/>).
		/// </summary>
		private const float PLAY_CHECK_INTERVAL = 0.02f;

		public Action OnAudioFinishedPlaying { get; set; }

		[SerializeField, Tooltip("If null, will look for a " + nameof(ITTSInputSource) + " object in its parents.")]
		private GameObject _ttsInputSourceObject;
		private ITTSInputSource _ttsInputSource;
		private AudioSource _audioSource;
		private Coroutine _audioPlayCoroutine;
		private bool _isPaused;

		private void Awake()
		{
			SetTTSReference();

			_ttsInputSource.OnTextChanged += () => Stop(true);
			_ttsInputSource.OnUIClosed += () => Stop(true);

			_audioSource = GetComponent<AudioSource>();
		}

		private void Start()
		{
			SoundSettingsManager.OnSoundSettingsChanged += SetVolume;
			SetVolume();
		}

		private void SetTTSReference()
		{
			if (_ttsInputSourceObject != null)
			{
				_ttsInputSource = TryGetTTSInputSource() as ITTSInputSource;
			}
			else
			{
				_ttsInputSource = GetComponentInParent<ITTSInputSource>();
			}
		}

		private void SetVolume()
		{
			_audioSource.volume = SoundSettingsManager.CurrentVolumeValue;
		}

		private void OnValidate()
		{
			if (_ttsInputSourceObject == null) return;

			var component = TryGetTTSInputSource();

			if (component == null)
			{
				_ttsInputSourceObject = null;
			}
			else
			{
				_ttsInputSourceObject = component.gameObject;
			}
		}

		private Component TryGetTTSInputSource()
		{
			return _ttsInputSourceObject.GetComponents<Component>().
				Where(c => c is ITTSInputSource).
				FirstOrDefault();
		}

		/// <summary>
		/// Play the audio from <see cref="ITTSInputSource.CurrentTTS"/>.
		/// </summary>
		public void PlayCurrent()
		{
			Stop();
			_audioPlayCoroutine = StartCoroutine(PlayAudio());
		}

		/// <summary>
		/// Resumes playing the audio, if the <see cref="_audioSource"/> was paused.
		/// </summary>
		public void Resume()
		{
			if (_audioPlayCoroutine != null)
			{
				_audioSource.UnPause();
				_isPaused = false;
			}
		}

		/// <summary>
		/// Pauses the audio that is currently playing.
		/// </summary>
		public void Pause()
		{
			if (_audioSource.isPlaying)
			{
				_audioSource.Pause();
				_isPaused = true;
			}
		}

		/// <summary>
		/// Stops the audio that is currently playing.
		/// </summary>
		public void Stop(bool notifyListeners = false)
		{
			if (_audioPlayCoroutine != null)
			{
				StopCoroutine(_audioPlayCoroutine);
				_audioPlayCoroutine = null;
			}

			if (_audioSource.isPlaying)
			{
				_audioSource.Stop();
				if (notifyListeners)
				{
					OnAudioFinishedPlaying?.Invoke();
				}
			}

			_isPaused = false;
		}

		private IEnumerator PlayAudio()
		{
			_isPaused = false;
			yield return null;

			foreach(var (clip, endDelay) in _ttsInputSource.CurrentTTS)
			{
				if (clip == null)
				{
					continue;
				}

				_audioSource.clip = clip;
				_audioSource.Play();

				_isPaused = false;

				while (_audioSource.isPlaying || _isPaused)
				{
					yield return new WaitForSeconds(PLAY_CHECK_INTERVAL);
				}

				// wait for the delay time if there is any
				if (endDelay > 0)
				{
					yield return new WaitForSeconds(endDelay);
				}
			}

			_audioPlayCoroutine = null;
			_isPaused = false;
			OnAudioFinishedPlaying?.Invoke();
		}
	}
}
