// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

using System.Text.Json;

namespace FolderHistory
{
	internal sealed class AppSettings
	{
		public bool HasBounds { get; set; }
		public int Left { get; set; }
		public int Top { get; set; }
		public int Width { get; set; } = 340;
		public int Height { get; set; } = 520;

		public bool TopMost { get; set; } = true;

		/// <summary>True once the first-run auto start choice was made, so later changes by the user are kept.</summary>
		public bool AutoStartConfigured { get; set; }

		public static AppSettings Load()
		{
			try
			{
				if (File.Exists(AppPaths.SettingsFile))
					return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new AppSettings();
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
			{
			}

			return new AppSettings();
		}

		public void Save()
		{
			try
			{
				Directory.CreateDirectory(AppPaths.DataDirectory);
				File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}
		}
	}
}
