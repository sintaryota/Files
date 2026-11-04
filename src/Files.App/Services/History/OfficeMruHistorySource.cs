// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

using Files.History;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Files.App.Services
{
	/// <summary>
	/// Imports file use from an Office application's "File MRU" registry lists. Pass another app name and source to support Excel and others.
	/// </summary>
	internal sealed partial class OfficeMruHistorySource(HistoryLog log, string appName, HistorySource source, int pollSeconds = HistoryDefaults.WordPollSeconds) : IDisposable
	{
		private readonly CancellationTokenSource cts = new();

		public void Start() => _ = Task.Run(PollAsync);

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
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Folder history stopped reading the {App} recent file list.", appName);
			}
		}

		private void ImportOnce()
		{
			try
			{
				ExternalImporter.Import(log, source, ReadEntries().ToList());
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "Folder history could not read the {App} recent file list.", appName);
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
