// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

using Microsoft.Win32;

namespace FolderHistory
{
	/// <summary>
	/// Starts the app when the user signs in to Windows, through the per-user Run key.
	/// </summary>
	internal static class AutoStart
	{
		private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
		private const string ValueName = "FolderHistory";

		public static bool IsEnabled
		{
			get
			{
				using var key = Registry.CurrentUser.OpenSubKey(RunKey);
				return key?.GetValue(ValueName) is string;
			}
		}

		public static void Set(bool enabled)
		{
			using var key = Registry.CurrentUser.CreateSubKey(RunKey);

			if (enabled && Environment.ProcessPath is { } exe)
				key.SetValue(ValueName, $"\"{exe}\" --background");
			else
				key.DeleteValue(ValueName, throwOnMissingValue: false);
		}
	}
}
