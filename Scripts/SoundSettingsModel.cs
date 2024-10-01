using System;

namespace T3FL.TTS.Settings
{
	[Serializable]
	public class SoundSettingsModel : BaseSoundSettingsModel
	{
		public VolumeSettings Volume = VolumeSettings.Medium;

		public void Copy(SoundSettingsModel other)
		{
			base.Copy(other);
			Volume = other.Volume;
		}
	}

	/// <summary>
	/// This model is used for downloading TTS audio clips, as the <see cref="SoundSettingsModel.Volume"/>
	/// member is not involved in that process.
	/// </summary>
	[Serializable]
	public class BaseSoundSettingsModel
	{
		public bool IsMale = true;
		public int Speed = 100;

		public void Copy(BaseSoundSettingsModel other)
		{
			IsMale = other.IsMale;
			Speed = other.Speed;
		}
	}
}