using System;
using System.Collections.Generic;

namespace T3FL.TTS.Settings
{
	public enum VolumeSettings { Low, Medium, High }

	public static class SoundSettingsManager
	{
		public static readonly SoundSettingsModel DEFAULT_SOUND_SETTINGS = new();
		/// <summary>
		/// The percentages for the TTS voice speed.
		/// </summary>
		public static readonly int[] SPEED_SETTINGS = new int[] { 50, 75, 100, 125, 150, 200 };
		private static readonly Dictionary<VolumeSettings, float> VOLUME_VALUES = new()
		{
			{ VolumeSettings.Low, 0.5f },
			{ VolumeSettings.Medium, 0.75f },
			{ VolumeSettings.High, 1 },
		};

		public static Action OnSoundSettingsChanged { get; set; }
		public static SoundSettingsModel SoundSettings { get; set; }
		public static float CurrentVolumeValue => VOLUME_VALUES[SoundSettings.Volume];

		public static void UpdateSoundSettings(SoundSettingsModel soundSettings)
		{
			SoundSettings = soundSettings;
			OnSoundSettingsChanged?.Invoke();
		}

		public static float GetVolume(VolumeSettings volumeSettings)
		{
			return VOLUME_VALUES[volumeSettings];
		}
	}
}