// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.History
{
	/// <summary>
	/// Tunable values for the folder history feature.
	/// </summary>
	public static class HistoryDefaults
	{
		/// <summary>Number of usage events kept.</summary>
		public const int HistorySize = 100;

		/// <summary>Number of most recently used folders that are always candidates.</summary>
		public const int RecentRequired = 5;

		/// <summary>Number of frequently used folders shown beside the recent ones.</summary>
		public const int FrequentTop = 10;

		/// <summary>Maximum number of leaf folders displayed.</summary>
		public const int MaxDisplay = 15;

		/// <summary>Window in seconds in which the same file from different sources counts once.</summary>
		public const int DedupeSeconds = 30;

		/// <summary>Interval in seconds to poll the Word recent file list.</summary>
		public const int WordPollSeconds = 60;
	}
}
