using static T3FL.TTS.TTSManager;

namespace T3FL.TTS.Download
{
	/// <summary>
	/// Support interface that is used in order to store requests of type <see cref="TTSRequestData"/> 
	/// and determine the order in which the <see cref="TTSDownloader"/> downloads the audio clips
	/// based on said requests.
	/// <br/><br/>
	/// Each class that extends this is free to decide how the requests are stored and managed 
	/// (see <see cref="TTSDownloadHelper"/> as an example).
	/// </summary>
	public interface ITTSDownloadHelper
	{
		/// <summary>
		/// The number of requests that are currently awaiting to be downloaded.
		/// </summary>
		int RequestCount { get; }

		/// <summary>
		/// </summary>
		/// <returns>the request that has the highest priority in the queue at the moment 
		/// when it is called.</returns>
		TTSRequestData GetHighestPriorityRequest();

		/// <summary>
		/// Removes the request with the given <paramref name="ID"/> from the queue and returns it.
		/// </summary>
		/// <param name="ID"></param>
		/// <returns></returns>
		TTSRequestData GetRequest(string ID);

		/// <summary>
		/// Adds the given <paramref name="request"/> to the queue, to be downloaded.
		/// </summary>
		/// <param name="request"></param>
		void AddRequestToQueue(TTSRequestData request);
	}
}