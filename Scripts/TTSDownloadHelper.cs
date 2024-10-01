using System;
using System.Collections.Generic;
using System.Linq;
using T3FL.TTS.Settings;
using static T3FL.TTS.TTSManager;

namespace T3FL.TTS.Download
{
	/// <summary>
	/// Support class that extends <see cref="ITTSDownloadHelper"/>.
	/// It is used in order to store requests of type <see cref="TTSRequestData"/>
	/// and determine the order in which the <see cref="TTSDownloader"/> downloads the audio clips
	/// based on said requests.
	/// <br/><br/>
	/// This process is done by ordering the IDs as they are to be used throughout the mission
	/// (using <see cref="IdComparer"/>), and then choosing the highest priority one dynamically
	/// based on the ID that has been used to trigger an interaction most recently (will choose
	/// the ID that either matches or comes after the most recently used one).
	/// <br/><br/>
	/// <b>WARNING: do not use the ‘_’, '[', and ']' characters in an ID, or the ‘.’ character in a tag.</b>
	/// </summary>
	public class TTSDownloadHelper : ITTSDownloadHelper
	{
		public static Action<string> OnInteractionTriggered;

		public int RequestCount => _requestQueue.Count;

		private SortedList<string, TTSRequestData> _requestQueue;
		private IdComparer _idComparer;
		private string _lastUsedId;

		public TTSDownloadHelper()
		{
			OnInteractionTriggered += UpdateLastUsedId;
			_idComparer = new IdComparer();
			_requestQueue = new SortedList<string, TTSRequestData>(_idComparer);
		}

		~TTSDownloadHelper()
		{
			OnInteractionTriggered -= UpdateLastUsedId;
		}

		public void AddRequestToQueue(TTSRequestData request)
		{
			_requestQueue.Add(request.ID, request);
		}

		public TTSRequestData GetHighestPriorityRequest()
		{
			if (_requestQueue.Count == 0) return null;

			if (string.IsNullOrEmpty(_lastUsedId))
			{
				// return the first request that matches the current accessibility settings
				foreach (var id in _requestQueue.Keys)
				{
					if (MatchesCurrentAccessibilitySettings(id))
					{
						return GetRequest(id);
					}
				}
			}

			// the IDs are going to be sorted based on the order in which they will be
			// used. go through all of them, and return the first one that matches or
			// comes after the last one that has been used in an interaction

			foreach (var (id, _) in _requestQueue)
			{
				if (MatchesCurrentAccessibilitySettings(id) && _idComparer.Compare(id, _lastUsedId) >= 0)
				{
					return GetRequest(id);
				}
			}

			// it shouldn't reach this point, but just to be sure, return the first ID

			return GetRequest(_requestQueue.First().Value.ID);
		}

		public TTSRequestData GetRequest(string id)
		{
			var request = _requestQueue[id];
			_requestQueue.Remove(request.ID);
			return request;
		}

		private void UpdateLastUsedId(string ID)
		{
			_lastUsedId = StringProcessor.ApplyAccessibilityMarkers(ID, SoundSettingsManager.SoundSettings.Speed);
		}

		/// <summary>
		/// Returns <i>true</i> if the accessibility settings contained by the given <paramref name="ID"/> match
		/// <b>all</b> relevant settings from <see cref="SoundSettingsManager.SoundSettings"/>.
		/// </summary>
		/// <param name="ID"></param>
		/// <returns></returns>
		private bool MatchesCurrentAccessibilitySettings(string ID)
		{
			var idData = _idComparer.GetComponents(ID);
			return idData.Speed == SoundSettingsManager.SoundSettings.Speed &&
				(!idData.IsMale.HasValue || idData.IsMale.Value == SoundSettingsManager.SoundSettings.IsMale);
		}

		/// <summary>
		/// Support class that is used by <see cref="TTSManager.TTSDownloader._requestsQueue"/> in order to sort
		/// requests based on a specific set of rules, which the regular string comparator doesn't do.
		/// <br/><br/>
		/// An ID must have the following structure: <b>[source_tag].[scenario].[mission].[order_in_mission]</b><br/>
		///	<list type="bullet">
		///	<item>source_tag: string; optional; refers to where the text came from (ex.: quiz, dialog, notebook)</item>
		///	<item>scenario: int; refers to the number of the scenario</item>
		///	<item>mission: int; refers to the number of the mission in its scenario</item>
		///	<item>order_in_mission: optional; one or multiple ints separated by '.'; refers to the order of this item in its mission</item>
		///	</list>
		/// <br/><br/>
		/// The IDs will be sorted by comparing their ints one by one. If any duplicates are found, their source_tag
		/// will be compared using <see cref="string.CompareTo(string)"/> in order to determine which one has priority.
		/// <list type="number">
		/// <item></item>
		/// </list>
		/// <br/><br/>
		/// <b>WARNING: do not use the ‘_’, '[', and ']' characters in an ID, or the ‘.’ character in a tag.</b>
		/// </summary>
		class IdComparer : IComparer<string>
		{
			private static char CLUSTER_SEPARATOR = '_';
			private static readonly char[] SEPARATORS = new char[] { '.', CLUSTER_SEPARATOR };

			private static Dictionary<string, IdData> _idDataCache = new Dictionary<string, IdData>();

			/// <summary>
			/// The method that is used by the <see cref="SortedList{TKey, TValue}"/> in order to sort
			/// its contents.
			/// </summary>
			/// <param name="id1"></param>
			/// <param name="id2"></param>
			/// <returns></returns>
			public int Compare(string id1, string id2)
			{
				// split both IDs into their components (tag, order array)
				var id1Components = GetComponents(id1);
				var id2Components = GetComponents(id2);

				// compare the accessibility speed settings
				if (id1Components.Speed != id2Components.Speed)
				{
					return id1Components.Speed.CompareTo(id2Components.Speed);
				}

				// compare the order arrays

				var minArrayLength = GetSmallest(id1Components.Order.Count, id2Components.Order.Count);

				// try to look for differences in their order
				for (var i = 0; i < minArrayLength; i++)
				{
					var id1Item = id1Components.Order[i];
					var id2Item = id2Components.Order[i];

					if (id1Item != id2Item)
					{
						return id1Item.CompareTo(id2Item);
					}
				}

				// so far, they have the same order

				// if they have a different amount of order numbers, the one with less components has priority
				if (id1Components.Order.Count != id2Components.Order.Count)
				{
					return -1 * id1Components.Order.Count.CompareTo(id2Components.Order.Count);
				}

				// at this point, the order arrays are identical

				// if they have different tags, determine their priorities using them
				if (id1Components.Tag != id2Components.Tag)
				{
					return id1Components.Tag.CompareTo(id2Components.Tag);
				}

				// they have the same tag and order, so they must be from the same cluster

				// check if both are part of clusters
				if (id1Components.OrderInCluster.HasValue && id2Components.OrderInCluster.HasValue)
				{
					if (id1Components.OrderInCluster.Value != id2Components.OrderInCluster.Value)
					{
						return id1Components.OrderInCluster.Value.CompareTo(id2Components.OrderInCluster.Value);
					}

					// else they have the same order in their cluster, therefore they're almost identical so far,
					// the only thing left to compare is the gender
					return Compare(id1Components.IsMale, id2Components.IsMale);
				}

				// else both IDs are almost identical so far, the only thing left to compare is the gender
				return Compare(id1Components.IsMale, id2Components.IsMale);

				int GetSmallest(int n1, int n2)
				{
					return n1 > n2 ? n2 : n1;
				}

				int Compare(bool? b1, bool? b2)
				{
					if (b1.HasValue)
					{
						if (b2.HasValue)
						{
							// both have value
							return b1.Value.CompareTo(b2.Value);
						}
						else
						{
							// only b1 has value
							return 1;
						}
					}
					else if (b2.HasValue)
					{
						// only b2 has value
						return -1;
					}

					// neither have value
					return 0;
				}
			}

			public IdData GetComponents(string id)
			{
				if (_idDataCache.ContainsKey(id))
				{
					return _idDataCache[id];
				}

				var idData = new IdData();
				idData.IsMale = StringProcessor.GetGender(id);
				idData.Speed = StringProcessor.GetSpeed(id);
				var hasCluster = id.Contains(CLUSTER_SEPARATOR);

				// remove the accessibility section before extracting the other
				// components (it's placed at the beginning between the '[' and ']' characters)
				var idComponents = StringProcessor.RemoveAccessibilityMarkers(id).Split(SEPARATORS);

				// iterate throught the components
				for (var i = 0; i < idComponents.Length; i++)
				{
					var item = idComponents[i];

					// check if the first element is a tag
					if (i == 0 && !int.TryParse(item, out _))
					{
						idData.Tag = item;
					}
					else
					{
						try
						{
							var number = int.Parse(item);

							if (i + 1 == idComponents.Length && hasCluster)
							{
								// the last element is the order in cluster
								idData.OrderInCluster = number;
							}
							else
							{
								// regular order number
								idData.Order.Add(number);
							}
						}
						catch
						{
							throw new Exception("The ID " + id + " does not have a proper structure. Couldn't parse" +
								"the " + item + " component to int.");
						}
					}
				}

				_idDataCache[id] = idData;
				return idData;
			}

			public class IdData
			{
				public float Speed { get; set; } = 1;
				public bool? IsMale { get; set; }
				public string Tag { get; set; } = string.Empty;
				public List<int> Order { get; set; } = new List<int>();
				public int? OrderInCluster { get; set; }
			}
		}
	}
}