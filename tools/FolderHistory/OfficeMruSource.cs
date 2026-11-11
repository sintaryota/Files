// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

using Files.History;
using Microsoft.Win32;

namespace FolderHistory
{
	/// <summary>
	/// Imports file use from an Office application's "File MRU" registry lists. Pass another app name and source to support Excel and others.
	/// </summary>
	internal sealed class OfficeMruSource(HistoryLog log, string appName, HistorySource source, int pollSeconds = HistoryDefaults.WordPollSeconds) : IDisposable
	{
		private readonly CancellationTokenSource cts = new();

		public void Start() => _ = Task.Run(PollAsync);

		public void ImportNow() => _ = Task.Run(ImportOnce);

		public void Dispose() => cts.Cancel();

		private async Task PollAsync()
		{
			try
			{
				ImportOnce();

				using var timer = new PeriodicTimer(TimeSpan.FromSeconds(pollSeconds));
				while (await timer.WaitForNextTickAsync(cts.Token).ConfigureAwait(false))
					ImportOnce();
			}
			catch (OperationCanceledException)
			{
			}
		}

		private void ImportOnce()
		{
			try
			{
				ExternalImporter.Import(log, source, ReadEntries().ToList());
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
			{
			}
		}

		private IEnumerable<ExternalEntry> ReadEntries()
		{
			var root = $@"Software\Microsoft\Office\16.0\{appName}";

			using (var userMru = Registry.CurrentUser.OpenSubKey($@"{root}\User MRU"))
			{
				foreach (var id in userMru?.GetSubKeyNames() ?? [])
				{
					using var key = userMru!.OpenSubKey($@"{id}\File MRU");
					foreach (var entry in ReadValues(key))
						yield return entry;
				}
			}

			using var legacy = Registry.CurrentUser.OpenSubKey($@"{root}\File MRU");
			foreach (var entry in ReadValues(legacy))
				yield return entry;
		}

		private static IEnumerable<ExternalEntry> ReadValues(RegistryKey? key)
		{
			if (key is null)
				yield break;

			foreach (var name in key.GetValueNames())
			{
				if (key.GetValue(name) is string value && OfficeMruParser.Parse(value) is { } entry)
					yield return entry;
			}
		}
	}
}
