// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Files.History
{
	/// <summary>
	/// Persists a <see cref="HistoryLog"/> as JSON. Writes are asynchronous and debounced.
	/// </summary>
	public sealed class HistoryStore
	{
		private const int CurrentVersion = 1;

		private readonly string filePath;
		private readonly TimeSpan debounce;
		private readonly Action<Exception>? onError;
		private readonly SemaphoreSlim writeLock = new(1, 1);
		private readonly object timerGate = new();
		private CancellationTokenSource? pending;

		public HistoryStore(string filePath, TimeSpan? debounce = null, Action<Exception>? onError = null)
		{
			this.filePath = filePath;
			this.debounce = debounce ?? TimeSpan.FromSeconds(1);
			this.onError = onError;
		}

		public string BackupPath => filePath + ".bak";

		/// <summary>
		/// Reads the file into <paramref name="log"/>. A missing file leaves it empty; a damaged file is moved to <c>.bak</c> and the log starts empty.
		/// </summary>
		public async Task LoadAsync(HistoryLog log)
		{
			string json;
			try
			{
				if (!File.Exists(filePath))
					return;

				json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				onError?.Invoke(ex);
				return;
			}

			try
			{
				var data = JsonSerializer.Deserialize(json, HistoryJsonContext.Default.HistoryFileDto)
					?? throw new JsonException("Empty history file.");
				if (data.Version != CurrentVersion)
					throw new JsonException($"Unsupported history version {data.Version}.");

				var events = (data.Events ?? []).Select(ToEvent).ToList();
				var imports = new Dictionary<HistorySource, DateTimeOffset>();
				foreach (var (key, value) in data.LastImported ?? [])
					imports[ParseSource(key)] = new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));

				log.Load(events, imports);
			}
			catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException)
			{
				onError?.Invoke(ex);
				Quarantine();
			}
		}

		/// <summary>
		/// Schedules a write after the debounce interval. Calls made in the meantime restart the interval.
		/// </summary>
		public void ScheduleSave(HistoryLog log)
		{
			CancellationTokenSource cts;
			lock (timerGate)
			{
				pending?.Cancel();
				pending?.Dispose();
				cts = pending = new CancellationTokenSource();
			}

			_ = Task.Run(async () =>
			{
				try
				{
					await Task.Delay(debounce, cts.Token).ConfigureAwait(false);
					await SaveAsync(log).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception ex)
				{
					onError?.Invoke(ex);
				}
			});
		}

		/// <summary>
		/// Writes immediately, skipping the debounce. Use on shutdown.
		/// </summary>
		public async Task SaveAsync(HistoryLog log)
		{
			var data = new HistoryFileDto
			{
				Version = CurrentVersion,
				LastImported = log.SnapshotImports().ToDictionary(kv => FormatSource(kv.Key), kv => kv.Value.UtcDateTime),
				Events = log.Snapshot().OrderBy(e => e.Time).Select(ToDto).ToList()
			};
			var json = JsonSerializer.Serialize(data, HistoryJsonContext.Default.HistoryFileDto);

			await writeLock.WaitAsync().ConfigureAwait(false);
			try
			{
				var directory = Path.GetDirectoryName(filePath);
				if (!string.IsNullOrEmpty(directory))
					Directory.CreateDirectory(directory);

				var temp = filePath + ".tmp";
				await File.WriteAllTextAsync(temp, json).ConfigureAwait(false);
				File.Move(temp, filePath, overwrite: true);
			}
			finally
			{
				writeLock.Release();
			}
		}

		private void Quarantine()
		{
			try
			{
				File.Move(filePath, BackupPath, overwrite: true);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				onError?.Invoke(ex);
			}
		}

		private static HistoryEvent ToEvent(HistoryEventDto d)
			=> new(
				new DateTimeOffset(DateTime.SpecifyKind(d.Time, DateTimeKind.Utc)),
				d.FilePath ?? throw new JsonException("filePath is missing."),
				d.Folder ?? throw new JsonException("folder is missing."),
				ParseSource(d.Source));

		private static HistoryEventDto ToDto(HistoryEvent e)
			=> new() { Time = e.Time.UtcDateTime, FilePath = e.FilePath, Folder = e.Folder, Source = FormatSource(e.Source) };

		private static string FormatSource(HistorySource source) => source switch
		{
			HistorySource.Files => "files",
			HistorySource.RecentItems => "recentItems",
			HistorySource.WordMru => "wordMru",
			_ => throw new ArgumentOutOfRangeException(nameof(source))
		};

		private static HistorySource ParseSource(string? value) => value switch
		{
			"files" => HistorySource.Files,
			"recentItems" => HistorySource.RecentItems,
			"wordMru" => HistorySource.WordMru,
			_ => throw new JsonException($"Unknown source '{value}'.")
		};
	}

	internal sealed class HistoryFileDto
	{
		public int Version { get; set; }
		public Dictionary<string, DateTime>? LastImported { get; set; }
		public List<HistoryEventDto>? Events { get; set; }
	}

	internal sealed class HistoryEventDto
	{
		public DateTime Time { get; set; }
		public string? FilePath { get; set; }
		public string? Folder { get; set; }
		public string? Source { get; set; }
	}

	[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
	[JsonSerializable(typeof(HistoryFileDto))]
	internal sealed partial class HistoryJsonContext : JsonSerializerContext
	{
	}
}
