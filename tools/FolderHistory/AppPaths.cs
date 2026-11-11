// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

namespace FolderHistory
{
	internal static class AppPaths
	{
		public static string DataDirectory { get; } = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"FolderHistory");

		public static string HistoryFile => Path.Combine(DataDirectory, "folder_history.json");

		public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

		public static string ReportFile => Path.Combine(DataDirectory, "report.txt");
	}
}
