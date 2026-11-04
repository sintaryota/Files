// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace FolderHistory
{
	internal static partial class NativeMethods
	{
		public const int WmHotKey = 0x0312;
		public const uint ModAlt = 0x1;
		public const uint ModControl = 0x2;
		public const uint ModNoRepeat = 0x4000;
		public const uint GwHwndNext = 2;
		public const int SwRestore = 9;

		[LibraryImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

		[LibraryImport("user32.dll")]
		public static partial IntPtr GetTopWindow(IntPtr hWnd);

		[LibraryImport("user32.dll")]
		public static partial IntPtr GetWindow(IntPtr hWnd, uint uCmd);

		[LibraryImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static partial bool SetForegroundWindow(IntPtr hWnd);

		[LibraryImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static partial bool IsIconic(IntPtr hWnd);

		[LibraryImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);
	}
}
