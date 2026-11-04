// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

namespace FolderHistory
{
	internal static class Program
	{
		private const string MutexName = @"Local\FolderHistory.SingleInstance";
		private const string ActivateEventName = @"Local\FolderHistory.Activate";

		[STAThread]
		private static void Main(string[] args)
		{
			using var mutex = new Mutex(true, MutexName, out var isFirstInstance);
			if (!isFirstInstance)
			{
				// Started again (for example from a shortcut): bring the running window to the front.
				try
				{
					using var running = EventWaitHandle.OpenExisting(ActivateEventName);
					running.Set();
				}
				catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException)
				{
				}

				return;
			}

			using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

			ApplicationConfiguration.Initialize();
			var background = args.Contains("--background", StringComparer.OrdinalIgnoreCase);
			Application.Run(new MainForm(activate, background));
		}
	}
}
