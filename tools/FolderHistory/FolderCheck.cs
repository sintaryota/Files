// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

namespace FolderHistory
{
	internal static class FolderCheck
	{
		private static readonly TimeSpan NetworkTimeout = TimeSpan.FromMilliseconds(500);

		/// <summary>
		/// Local folders are checked directly. A network folder that does not answer in time counts as existing, so a slow share never stalls the app.
		/// </summary>
		public static bool Exists(string path)
		{
			try
			{
				if (!IsNetworkPath(path))
					return Directory.Exists(path);

				var check = Task.Run(() => Directory.Exists(path));
				return !check.Wait(NetworkTimeout) || check.Result;
			}
			catch (Exception)
			{
				return true;
			}
		}

		private static bool IsNetworkPath(string path)
		{
			if (path.StartsWith(@"\\", StringComparison.Ordinal))
				return true;

			var root = Path.GetPathRoot(path);
			return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network;
		}
	}
}
