// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.History;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;
using System.Text;

namespace Files.App.ViewModels.UserControls
{
	public sealed partial class SidebarViewModel
	{
		// Temporary: shows a row at the end of the History section that explains why folders are listed. Set to false, or delete this file's diagnostics members, to remove it.
		private const bool HistoryDiagnosticsEnabled = true;
		private const string HistoryDiagnosticsPath = "history:diagnostics";

		private static readonly TimeSpan HistoryRebuildDebounce = TimeSpan.FromSeconds(1);
		private static readonly TimeSpan NetworkExistsTimeout = TimeSpan.FromMilliseconds(500);

		private readonly object historyGate = new();
		private IFolderHistoryService? folderHistoryService;
		private CancellationTokenSource? historyDebounce;
		private string? historySignature;
		private string? historyExpandedForPath;

		private void InitializeHistorySection()
		{
			folderHistoryService = Ioc.Default.GetRequiredService<IFolderHistoryService>();
			folderHistoryService.Log.Changed += (_, _) => ScheduleHistoryRebuild();
			ScheduleHistoryRebuild();
		}

		// Recomputes at most once per second however many events arrive.
		private void ScheduleHistoryRebuild()
		{
			CancellationTokenSource cts;
			lock (historyGate)
			{
				historyDebounce?.Cancel();
				cts = historyDebounce = new CancellationTokenSource();
			}

			_ = Task.Run(async () =>
			{
				try
				{
					await Task.Delay(HistoryRebuildDebounce, cts.Token);
					await RebuildHistorySectionAsync();
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception ex)
				{
					App.Logger.LogWarning(ex, "The folder history section could not be rebuilt.");
				}
			});
		}

		private async Task RebuildHistorySectionAsync()
		{
			if (folderHistoryService is null || dispatcherQueue is null)
				return;

			var selected = FolderSelector.Select(folderHistoryService.Log.Snapshot(), HistoryFolderExists);
			var tree = HistoryTreeBuilder.Build(selected);
			var signature = GetTreeSignature(tree);
			var iconBytes = await LocationItem.GetGenericSmallFolderIconBytesAsync();

			await dispatcherQueue.EnqueueOrInvokeAsync(async () =>
			{
				if (tree.Count == 0)
				{
					historySignature = null;
					if (GetSection(SectionType.History) is { } empty)
						sidebarItems.Remove(empty);
					return;
				}

				var section = await GetOrCreateSectionAsync(SectionType.History);
				if (section?.ChildItems is not { } roots)
					return;

				// Rebuilding resets what the user opened and can swallow a click, so leave the rows alone when nothing changed.
				if (signature == historySignature && roots.Count > 0)
					return;

				BitmapImage? icon = null;
				if (iconBytes is not null)
				{
					try { icon = await iconBytes.ToBitmapAsync(); }
					catch (Exception ex) { App.Logger.LogDebug(ex, "The folder history icon could not be decoded."); }
				}

				var expanded = new HashSet<string>(HistoryComparer);
				CollectExpandedPaths(roots, expanded);

				var items = tree.Select(node => CreateHistoryItem(node, icon)).ToList<INavigationControlItem>();
				if (HistoryDiagnosticsEnabled)
					items.Add(CreateHistoryDiagnosticsItem());
				RestoreExpandedPaths(items, expanded);

				roots.BeginBulkOperation();
				try
				{
					roots.Clear();
					roots.AddRange(items);
				}
				finally
				{
					roots.EndBulkOperation();
				}

				historySignature = signature;
				ApplyHistoryExpansion(closeOthers: false);
			});
		}

		private static readonly StringComparer HistoryComparer = StringComparer.OrdinalIgnoreCase;

		private static string GetTreeSignature(IEnumerable<HistoryNode> nodes)
			=> string.Join('|', nodes.Select(n => $"{n.FullPath}\u0001{n.Name}\u0001{(n.IsLeaf ? 1 : 0)}[{GetTreeSignature(n.Children)}]"));

		private static LocationItem CreateHistoryItem(HistoryNode node, BitmapImage? icon)
		{
			var item = new LocationItem
			{
				Path = node.FullPath,
				Text = node.Name,
				Section = SectionType.History,
				Icon = icon,
				MenuOptions = new ContextMenuOptions
				{
					IsLocationItem = true,
					ShowProperties = true,
					ShowShellItems = true,
				},
			};

			// Children are the compressed history nodes, not a lazy listing of the real subfolders.
			if (node.Children.Count > 0)
			{
				item.ChildItems = [];
				item.ChildItems.AddRange(node.Children.Select(child => CreateHistoryItem(child, icon)));
			}

			return item;
		}

		private static void CollectExpandedPaths(IEnumerable<INavigationControlItem> items, HashSet<string> sink)
		{
			foreach (var item in items)
			{
				if (item.IsExpanded && item.Path is { } path)
					sink.Add(path);
				if (item.Children is IEnumerable<INavigationControlItem> children)
					CollectExpandedPaths(children, sink);
			}
		}

		private static void RestoreExpandedPaths(IEnumerable<INavigationControlItem> items, HashSet<string> expanded)
		{
			foreach (var item in items)
			{
				item.IsExpanded = item.Path is { } path && expanded.Contains(path);
				if (item.Children is IEnumerable<INavigationControlItem> children)
					RestoreExpandedPaths(children, expanded);
			}
		}

		// Opens the route down to the folder being shown. With closeOthers, everything off that route is closed too, but only when the folder changed: the sidebar re-checks the current folder after every expand, and redoing it then would close what the user just opened. A folder outside the tree leaves the state as it is.
		private void ApplyHistoryExpansion(bool closeOthers = true)
		{
			var path = CurrentPath;
			if (string.IsNullOrEmpty(path) ||
				GetSection(SectionType.History)?.ChildItems is not { } roots)
				return;

			if (closeOthers && string.Equals(path, historyExpandedForPath, StringComparison.OrdinalIgnoreCase))
				return;

			var route = FindHistoryRoute(roots, path);
			if (route is null)
				return;

			if (closeOthers)
				historyExpandedForPath = path;

			var open = new HashSet<INavigationControlItem>(route.Take(route.Count - 1));
			SetHistoryExpansion(roots, open, closeOthers);
		}

		private static void SetHistoryExpansion(IEnumerable<INavigationControlItem> items, HashSet<INavigationControlItem> open, bool closeOthers)
		{
			foreach (var item in items)
			{
				item.IsExpanded = open.Contains(item) || (!closeOthers && item.IsExpanded);
				if (item.Children is IEnumerable<INavigationControlItem> children)
					SetHistoryExpansion(children, open, closeOthers);
			}
		}

		private static List<INavigationControlItem>? FindHistoryRoute(IEnumerable<INavigationControlItem> items, string path)
		{
			foreach (var item in items)
			{
				if (!PathIsAncestorOrMatch(item.Path, path))
					continue;

				var route = new List<INavigationControlItem> { item };
				if (item.Children is IEnumerable<INavigationControlItem> children &&
					FindHistoryRoute(children, path) is { } deeper)
					route.AddRange(deeper);

				return route;
			}

			return null;
		}

		// Local paths are checked directly. Network paths are given a short time to answer and shown if they do not, so a slow share never stalls the sidebar.
		private static bool HistoryFolderExists(string path)
		{
			try
			{
				if (!IsNetworkPath(path))
					return Directory.Exists(path);

				var check = Task.Run(() => Directory.Exists(path));
				return !check.Wait(NetworkExistsTimeout) || check.Result;
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

			var root = System.IO.Path.GetPathRoot(path);
			return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == System.IO.DriveType.Network;
		}

		#region Diagnostics (temporary)

		private static LocationItem CreateHistoryDiagnosticsItem()
		{
			var item = new LocationItem
			{
				Path = HistoryDiagnosticsPath,
				Text = "履歴の診断（一時）",
				Section = SectionType.History,
				SelectsOnInvoked = false,
				MenuOptions = new ContextMenuOptions(),
			};
			item.ToolTip = "履歴に載った理由、載らなかった理由をテキストで表示します（一時的な機能）";
			return item;
		}

		private static bool IsHistoryDiagnosticsItem(INavigationControlItem item)
			=> HistoryDiagnosticsEnabled &&
				item is LocationItem { Section: SectionType.History } &&
				string.Equals(item.Path, HistoryDiagnosticsPath, StringComparison.Ordinal);

		// Writes the report to the app's local folder and opens it in the default text editor.
		private async Task ShowHistoryDiagnosticsAsync()
		{
			try
			{
				if (folderHistoryService is null)
					return;

				var log = folderHistoryService.Log;
				var text = await Task.Run(() =>
				{
					var report = FolderSelector.SelectWithReport(log.Snapshot(), HistoryFolderExists);
					var tree = HistoryTreeBuilder.Build(report.Selected);
					return HistoryDiagnostics.Format(log, report, tree, DateTimeOffset.UtcNow);
				});

				var path = System.IO.Path.Combine(global::Windows.Storage.ApplicationData.Current.LocalFolder.Path, "folder_history_report.txt");
				await File.WriteAllTextAsync(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

				var file = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(path);
				await global::Windows.System.Launcher.LaunchFileAsync(file);
			}
			catch (Exception ex)
			{
				App.Logger.LogWarning(ex, "The folder history report could not be shown.");
			}
			finally
			{
				// Invoking the row selected it; put the selection back on the folder being shown.
				dispatcherQueue?.TryEnqueue(() => UpdateSidebarSelectedItemFromArgs(CurrentPath));
			}
		}

		#endregion
	}
}
