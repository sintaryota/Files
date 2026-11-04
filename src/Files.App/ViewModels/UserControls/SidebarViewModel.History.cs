// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.History;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;

namespace Files.App.ViewModels.UserControls
{
	public sealed partial class SidebarViewModel
	{
		private static readonly TimeSpan HistoryRebuildDebounce = TimeSpan.FromSeconds(1);
		private static readonly TimeSpan NetworkExistsTimeout = TimeSpan.FromMilliseconds(500);

		private readonly object historyGate = new();
		private IFolderHistoryService? folderHistoryService;
		private CancellationTokenSource? historyDebounce;

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
			var iconBytes = await LocationItem.GetGenericSmallFolderIconBytesAsync();

			await dispatcherQueue.EnqueueOrInvokeAsync(async () =>
			{
				if (tree.Count == 0)
				{
					if (GetSection(SectionType.History) is { } empty)
						sidebarItems.Remove(empty);
					return;
				}

				var section = await GetOrCreateSectionAsync(SectionType.History);
				if (section?.ChildItems is not { } roots)
					return;

				BitmapImage? icon = null;
				if (iconBytes is not null)
				{
					try { icon = await iconBytes.ToBitmapAsync(); }
					catch (Exception ex) { App.Logger.LogDebug(ex, "The folder history icon could not be decoded."); }
				}

				var items = tree.Select(node => CreateHistoryItem(node, icon)).ToList();

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

				ApplyHistoryExpansion();
			});
		}

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

		// Expands only the route down to the folder being shown, and closes the rest. A folder outside the tree leaves the state as it is.
		private void ApplyHistoryExpansion()
		{
			var path = CurrentPath;
			if (string.IsNullOrEmpty(path) ||
				GetSection(SectionType.History)?.ChildItems is not { } roots)
				return;

			var route = FindHistoryRoute(roots, path);
			if (route is null)
				return;

			var open = new HashSet<INavigationControlItem>(route.Take(route.Count - 1));
			SetHistoryExpansion(roots, open);
		}

		private static void SetHistoryExpansion(IEnumerable<INavigationControlItem> items, HashSet<INavigationControlItem> open)
		{
			foreach (var item in items)
			{
				item.IsExpanded = open.Contains(item);
				if (item.Children is IEnumerable<INavigationControlItem> children)
					SetHistoryExpansion(children, open);
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
	}
}
