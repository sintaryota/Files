// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace FolderHistory
{
	/// <summary>
	/// Sends a folder to an Explorer window that is already open, instead of starting a new one.
	/// </summary>
	internal static class ExplorerNavigator
	{
		/// <returns>False when no Explorer window could be used; the caller then opens a new one.</returns>
		public static bool TryNavigate(string path)
		{
			try
			{
				var type = Type.GetTypeFromProgID("Shell.Application");
				if (type is null)
					return false;

				dynamic? shell = Activator.CreateInstance(type);
				if (shell is null)
					return false;

				dynamic windows = shell.Windows();
				int count = windows.Count;

				var candidates = new List<(IntPtr Handle, object Window)>();
				for (var i = 0; i < count; i++)
				{
					try
					{
						dynamic? window = windows.Item(i);
						if (window is null)
							continue;

						string fullName = window.FullName ?? string.Empty;
						if (!fullName.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase))
							continue;

						candidates.Add((new IntPtr((long)window.HWND), (object)window));
					}
					catch (COMException)
					{
					}
				}

				if (candidates.Count == 0)
					return false;

				var chosen = FindTopmost(candidates.Select(c => c.Handle).ToHashSet()) is var top && top != IntPtr.Zero
					? candidates.First(c => c.Handle == top)
					: candidates[0];

				((dynamic)chosen.Window).Navigate2(path);
				BringToFront(chosen.Handle);
				return true;
			}
			catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
			{
				return false;
			}
		}

		// Walks the windows from the top of the z-order and returns the first one that is an Explorer window.
		private static IntPtr FindTopmost(HashSet<IntPtr> handles)
		{
			var window = NativeMethods.GetTopWindow(IntPtr.Zero);
			while (window != IntPtr.Zero)
			{
				if (handles.Contains(window))
					return window;

				window = NativeMethods.GetWindow(window, NativeMethods.GwHwndNext);
			}

			return IntPtr.Zero;
		}

		private static void BringToFront(IntPtr handle)
		{
			if (NativeMethods.IsIconic(handle))
				NativeMethods.ShowWindow(handle, NativeMethods.SwRestore);

			NativeMethods.SetForegroundWindow(handle);
		}
	}
}
