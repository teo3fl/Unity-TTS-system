using System;
using System.Collections.Generic;
using UnityEngine;

namespace T3FL.TTS.Utils
{
	/// <summary>
	/// Base for objects that need to have TTS functionality. Usually used in combination with a
	/// <see cref="TTSCacheHandler"/> in order to manage the <see cref="CurrentTTS"/> list, and
	/// a <see cref="TTSSpeakerController"/> object.
	/// </summary>
	public interface ITTSInputSource
	{
		/// <summary>
		/// Returns the audio clips of all text elements that are currently visible on
		/// this object.
		///	The <i>EndDelay</i> float refers to the amount of time that should be waited
		///	for at the end of the current clip and before playing the next one.
		/// </summary>
		IEnumerable<(AudioClip Audio, float EndDelay)> CurrentTTS { get; }

		/// <summary>
		/// Invoked when any of the relevant text (used in TTS) from the UI changes.
		/// </summary>
		Action OnTextChanged { get; set; }

		/// <summary>
		/// Invoked when the UI is hidden is any way.
		/// </summary>
		Action OnUIClosed { get; set; }
	}
}
