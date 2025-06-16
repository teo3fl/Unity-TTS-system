using System;
using System.Collections.Generic;

namespace T3FL.TTS
{
	[Serializable]
	public class TTSVoiceModel
	{
		/// <summary>
		/// The actual in-game characters that will speak with this <see cref="TTSVoiceModel.Voice"/>.
		/// </summary>
		public List<string> Characters = new List<string>();
		/// <summary>
		/// Should match <see cref="Meta.WitAi.TTS.Data.TTSVoiceSettings.SettingsId"/>.
		/// <br/><br/>
		/// For the available voice list, see the bottom of this .cs file.
		/// </summary>
		public string Voice;
		/// <summary>
		/// Determines the voice modifier. If null or empty, the voice will be left as it is. 
		/// <br/><br/>
		/// For the available style list, see the bottom of this .cs file.
		/// </summary>
		public string Style;
	}
}
