// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text;
using Files.History;

namespace FolderHistory
{
	internal sealed class MainForm : Form
	{
		private const string PinGroupTag = "pin:group";
		private const int MaxPinned = 4;
		private const int HotkeyId = 1;
		private const uint HotkeyVirtualKey = 0x48; // H
		private const int EdgeShowTicks = 2;
		private const int EdgeHideTicks = 6;

		// Folders directly under the user profile are shown as parents, so the profile itself is not listed.
		private static readonly string[] BaseFolders = [Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)];

		private readonly EventWaitHandle activateSignal;
		private readonly bool startHidden;
		private readonly AppSettings settings = AppSettings.Load();
		private readonly HistoryLog log = new();
		private readonly HistoryStore store = new(AppPaths.HistoryFile);
		private readonly TreeView tree = new();
		private readonly ToolStripStatusLabel statusLabel = new();
		private readonly ToolStripButton topMostButton = new("常に手前") { CheckOnClick = true };
		private readonly ToolStripButton autoStartButton = new("自動起動") { CheckOnClick = true };
		private readonly ToolStripButton reuseButton = new("既存の窓") { CheckOnClick = true };
		private readonly ToolStripButton edgeButton = new("左端") { CheckOnClick = true };
		private readonly ToolStripMenuItem pinItem = new("ピン留めに追加");
		private readonly ToolStripMenuItem unpinItem = new("ピン留めを外す");
		private readonly NotifyIcon tray = new();
		private readonly System.Windows.Forms.Timer rebuildTimer = new() { Interval = 1000 };
		private readonly System.Windows.Forms.Timer edgeTimer = new() { Interval = 100 };
		private readonly HashSet<string> collapsed = new(HistoryPath.Comparer);

		private RegisteredWaitHandle? activateWait;
		private RecentItemsSource? recentSource;
		private OfficeMruSource? wordSource;
		private string? signature;
		private IReadOnlyList<HistoryNode> currentNodes = [];
		private bool firstTree = true;
		private bool visibleDecided;
		private bool initStarted;
		private bool building;
		private bool rebuilding;
		private bool rebuildAgain;
		private bool suppressToggle;
		private bool exiting;
		private bool menuOpen;
		private int modalDepth;
		private int edgeShowTicks;
		private int edgeHideTicks;

		public MainForm(EventWaitHandle activateSignal, bool startHidden)
		{
			this.activateSignal = activateSignal;
			this.startHidden = startHidden;

			Text = "Folder History";
			Icon = SystemIcons.Application;
			MinimumSize = new Size(240, 240);
			if (SystemFonts.MessageBoxFont is { } font)
				Font = font;

			StartPosition = FormStartPosition.Manual;
			ApplySavedBounds();
			TopMost = settings.TopMost || settings.EdgeMode;

			ConfigureAutoStartOnFirstRun();
			BuildUi();
			BuildTray();

			// The app's own data folder holds this history and the report; opening them must not count as use.
			var dataFolder = HistoryPath.Normalize(AppPaths.DataDirectory);
			log.Exclude = path => HistoryPath.IsAncestor(dataFolder, path);

			rebuildTimer.Tick += OnRebuildTick;
			edgeTimer.Tick += OnEdgeTick;
			edgeTimer.Start();
			Activated += OnFormActivated;
		}

		// A hidden start (auto start) shows only the tray icon; the window appears when asked for.
		protected override void SetVisibleCore(bool value)
		{
			if (!visibleDecided)
			{
				visibleDecided = true;
				if (startHidden)
				{
					if (!IsHandleCreated)
						CreateHandle();
					base.SetVisibleCore(false);
					return;
				}
			}

			base.SetVisibleCore(value);
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);

			RegisterHotkey();

			if (initStarted)
				return;

			initStarted = true;
			BeginInvoke((Action)(() => _ = InitializeAsync()));
		}

		protected override void WndProc(ref Message m)
		{
			if (m.Msg == NativeMethods.WmHotKey && m.WParam.ToInt32() == HotkeyId)
			{
				ToggleWindow();
				return;
			}

			base.WndProc(ref m);
		}

		// Ctrl + Alt + H shows or hides the window from anywhere.
		private void RegisterHotkey()
		{
			var modifiers = NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat;
			if (!NativeMethods.RegisterHotKey(Handle, HotkeyId, modifiers, HotkeyVirtualKey))
				statusLabel.Text = "Ctrl+Alt+H は他のアプリが使っています";
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if (!exiting && e.CloseReason == CloseReason.UserClosing)
			{
				e.Cancel = true;
				SaveBounds();
				Hide();
				return;
			}

			base.OnFormClosing(e);
		}

		protected override void OnFormClosed(FormClosedEventArgs e)
		{
			base.OnFormClosed(e);

			try
			{
				SaveBounds();
				store.SaveAsync(log).GetAwaiter().GetResult();
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}

			activateWait?.Unregister(null);
			recentSource?.Dispose();
			wordSource?.Dispose();
			rebuildTimer.Dispose();
			edgeTimer.Dispose();
			tray.Visible = false;
			tray.Dispose();
		}

		private void BuildUi()
		{
			var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };

			var refreshButton = new ToolStripButton("更新");
			refreshButton.Click += (_, _) => RefreshNow();

			topMostButton.Checked = settings.TopMost;
			topMostButton.CheckedChanged += (_, _) =>
			{
				TopMost = topMostButton.Checked || settings.EdgeMode;
				settings.TopMost = TopMost;
				settings.Save();
			};

			autoStartButton.Checked = AutoStart.IsEnabled;
			autoStartButton.CheckedChanged += (_, _) =>
			{
				try
				{
					AutoStart.Set(autoStartButton.Checked);
				}
				catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
				{
					ShowMessage("自動起動の設定を変更できませんでした。", MessageBoxIcon.Warning);
				}
			};

			var addButton = new ToolStripButton("追加…") { ToolTipText = "フォルダをピン留めに追加します（最大 4 件）。フォルダをウィンドウへドラッグしても追加できます" };
			addButton.Click += (_, _) => AddPinnedFolderFromDialog();

			reuseButton.Checked = settings.ReuseExplorer;
			reuseButton.ToolTipText = "オン: 開いているエクスプローラーをそのフォルダへ移動します。オフ: 常に新しいウィンドウで開きます";
			reuseButton.CheckedChanged += (_, _) =>
			{
				settings.ReuseExplorer = reuseButton.Checked;
				settings.Save();
			};

			edgeButton.Checked = settings.EdgeMode;
			edgeButton.ToolTipText = "オン: 画面の左端にマウスを当てると表示し、離れると隠します";
			edgeButton.CheckedChanged += (_, _) => ApplyEdgeMode(edgeButton.Checked);

			var diagnosticsButton = new ToolStripButton("診断");
			diagnosticsButton.Click += async (_, _) => await ShowDiagnosticsAsync();

			toolbar.Items.AddRange(new ToolStripItem[]
			{
				refreshButton,
				addButton,
				new ToolStripSeparator(),
				reuseButton,
				edgeButton,
				topMostButton,
				autoStartButton,
				new ToolStripSeparator(),
				diagnosticsButton,
			});

			tree.Dock = DockStyle.Fill;
			tree.ShowNodeToolTips = true;
			tree.HideSelection = false;
			tree.AllowDrop = true;
			tree.DragEnter += OnTreeDragOver;
			tree.DragOver += OnTreeDragOver;
			tree.DragDrop += OnTreeDrop;
			tree.NodeMouseDoubleClick += (_, e) => OpenInExplorer(e.Node);
			tree.MouseDown += (_, e) => suppressToggle = e.Clicks >= 2;
			tree.BeforeExpand += OnBeforeToggle;
			tree.BeforeCollapse += OnBeforeToggle;
			tree.AfterExpand += (_, e) => TrackExpansion(e.Node, expanded: true);
			tree.AfterCollapse += (_, e) => TrackExpansion(e.Node, expanded: false);
			tree.NodeMouseClick += (_, e) =>
			{
				if (e.Button == MouseButtons.Right)
					tree.SelectedNode = e.Node;
			};
			tree.KeyDown += (_, e) =>
			{
				if (e.KeyCode == Keys.Enter && tree.SelectedNode is { } node)
				{
					OpenInExplorer(node);
					e.Handled = true;
					e.SuppressKeyPress = true;
				}
				else if (e.KeyCode == Keys.F5)
				{
					RefreshNow();
					e.Handled = true;
				}
				else if (e.KeyCode == Keys.Delete && tree.SelectedNode is { } selected && IsPinned(selected))
				{
					Unpin((string)selected.Tag!);
					e.Handled = true;
				}
			};

			var menu = new ContextMenuStrip();
			menu.Items.Add("エクスプローラーで開く", null, (_, _) => OpenInExplorer(tree.SelectedNode));
			menu.Items.Add("新しいウィンドウで開く", null, (_, _) => OpenInExplorer(tree.SelectedNode, forceNewWindow: true));
			menu.Items.Add("パスをコピー", null, (_, _) => CopyPath(tree.SelectedNode));
			menu.Items.Add(new ToolStripSeparator());
			pinItem.Click += (_, _) =>
			{
				if (GetPath(tree.SelectedNode) is { } path)
					Pin(path);
			};
			unpinItem.Click += (_, _) =>
			{
				if (GetPath(tree.SelectedNode) is { } path)
					Unpin(path);
			};
			menu.Items.Add(pinItem);
			menu.Items.Add(unpinItem);
			menu.Opening += (_, e) =>
			{
				var node = tree.SelectedNode;
				if (GetPath(node) is null)
				{
					e.Cancel = true;
					return;
				}

				var pinned = IsPinned(node!);
				pinItem.Visible = !pinned;
				unpinItem.Visible = pinned;
				menuOpen = true;
			};
			menu.Closed += (_, _) => menuOpen = false;
			tree.ContextMenuStrip = menu;

			var status = new StatusStrip();
			status.Items.Add(statusLabel);

			Controls.Add(tree);
			Controls.Add(toolbar);
			Controls.Add(status);
		}

		private void BuildTray()
		{
			tray.Icon = Icon;
			tray.Text = "Folder History";

			var menu = new ContextMenuStrip();
			menu.Items.Add("表示", null, (_, _) => ShowWindow());
			menu.Items.Add("終了", null, (_, _) => ExitApp());
			tray.ContextMenuStrip = menu;

			tray.MouseClick += (_, e) =>
			{
				if (e.Button == MouseButtons.Left)
					ToggleWindow();
			};

			tray.Visible = true;
		}

		// The first run turns auto start on; after that the user's choice is kept.
		private void ConfigureAutoStartOnFirstRun()
		{
			if (settings.AutoStartConfigured)
				return;

			try
			{
				AutoStart.Set(true);
			}
			catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
			{
			}

			settings.AutoStartConfigured = true;
			settings.Save();
		}

		private async Task InitializeAsync()
		{
			try
			{
				await store.LoadAsync(log);

				log.Changed += (_, _) => OnLogChanged();

				recentSource = new RecentItemsSource(log);
				wordSource = new OfficeMruSource(log, "Word", HistorySource.WordMru);
				recentSource.Start();
				wordSource.Start();

				// A second start (a shortcut, for example) signals this instance to come to the front.
				activateWait = ThreadPool.RegisterWaitForSingleObject(
					activateSignal,
					(_, _) =>
					{
						try
						{
							BeginInvoke((Action)ShowWindow);
						}
						catch (InvalidOperationException)
						{
						}
					},
					null,
					Timeout.Infinite,
					executeOnlyOnce: false);

				await RebuildAsync();
			}
			catch (Exception ex)
			{
				statusLabel.Text = "初期化に失敗しました: " + ex.Message;
			}
		}

		private void OnLogChanged()
		{
			store.ScheduleSave(log);

			if (!IsHandleCreated || IsDisposed)
				return;

			try
			{
				BeginInvoke((Action)ScheduleRebuild);
			}
			catch (InvalidOperationException)
			{
			}
		}

		// Recomputes at most once per second however many events arrive.
		private void ScheduleRebuild()
		{
			rebuildTimer.Stop();
			rebuildTimer.Start();
		}

		private async void OnRebuildTick(object? sender, EventArgs e)
		{
			rebuildTimer.Stop();

			try
			{
				await RebuildAsync();
			}
			catch (Exception ex)
			{
				statusLabel.Text = "更新に失敗しました: " + ex.Message;
			}
		}

		private void RefreshNow()
		{
			recentSource?.ImportNow();
			wordSource?.ImportNow();
			ScheduleRebuild();
		}

		private async Task RebuildAsync()
		{
			if (rebuilding)
			{
				rebuildAgain = true;
				return;
			}

			rebuilding = true;
			try
			{
				do
				{
					rebuildAgain = false;

					var events = log.Snapshot();
					var pins = settings.PinnedFolders.ToList();
					var (nodes, newSignature, folderCount) = await Task.Run(() =>
					{
						var selected = FolderSelector.Select(events, FolderCheck.Exists);
						var built = HistoryTreeBuilder.Build(selected, BaseFolders);
						return (built, string.Join('|', pins) + "#" + GetSignature(built), selected.Count);
					});

					currentNodes = nodes;

					// Rebuilding resets the rows, so leave them alone when nothing changed.
					if (newSignature != signature)
					{
						signature = newSignature;
						ApplyTree(pins, nodes);

						if (firstTree)
						{
							firstTree = false;
							ApplyActiveRoute();
						}
					}

					statusLabel.Text = $"{folderCount} フォルダ / 履歴 {events.Count} 件 / {DateTime.Now:HH:mm:ss}";
				}
				while (rebuildAgain);
			}
			finally
			{
				rebuilding = false;
			}
		}

	#region Active route

		private void OnFormActivated(object? sender, EventArgs e)
		{
			// Coming back from a dialog or a menu must not undo what the user just opened.
			if (modalDepth > 0 || menuOpen)
				return;

			ApplyActiveRoute();
		}

		// Opens only the route to the folder used most recently and the route to the folder Explorer shows; every other parent is folded.
		private void ApplyActiveRoute()
		{
			var open = new HashSet<string>(HistoryPath.Comparer);

			if (FindNewestRoute(currentNodes) is { } newest)
			{
				foreach (var node in newest)
					open.Add(node.FullPath);
			}

			if (ExplorerNavigator.GetCurrentFolder() is { } folder)
				AddRouteToPath(currentNodes, folder, open);

			building = true;
			tree.BeginUpdate();
			try
			{
				foreach (TreeNode root in tree.Nodes)
					ApplyRoute(root, open);
			}
			finally
			{
				tree.EndUpdate();
				building = false;
			}
		}

		private void ApplyRoute(TreeNode node, HashSet<string> open)
		{
			if (node.Tag is not string tag)
				return;

			// Pinned folders have no children to fold.
			if (tag == PinGroupTag)
			{
				collapsed.Remove(tag);
				node.Expand();
				return;
			}

			if (open.Contains(tag))
			{
				collapsed.Remove(tag);
				node.Expand();
			}
			else
			{
				collapsed.Add(tag);
				node.Collapse();
			}

			foreach (TreeNode child in node.Nodes)
				ApplyRoute(child, open);
		}

		// The nodes from the top down to the folder with the latest use.
		private static List<HistoryNode>? FindNewestRoute(IEnumerable<HistoryNode> nodes)
		{
			List<HistoryNode>? best = null;

			foreach (var node in nodes)
			{
				List<HistoryNode>? route;
				if (node.Children.Count == 0)
				{
					route = [node];
				}
				else if (FindNewestRoute(node.Children) is { } inner)
				{
					inner.Insert(0, node);
					route = inner;
				}
				else
				{
					continue;
				}

				if (best is null || route[^1].LastUsed > best[^1].LastUsed)
					best = route;
			}

			return best;
		}

		private static void AddRouteToPath(IEnumerable<HistoryNode> nodes, string path, HashSet<string> open)
		{
			string normalized;
			try
			{
				normalized = HistoryPath.Normalize(path);
			}
			catch (ArgumentException)
			{
				return;
			}

			AddRoute(nodes, normalized, open);

			static void AddRoute(IEnumerable<HistoryNode> nodes, string target, HashSet<string> open)
			{
				foreach (var node in nodes)
				{
					if (!HistoryPath.Comparer.Equals(node.FullPath, target) && !HistoryPath.IsAncestor(node.FullPath, target))
						continue;

					open.Add(node.FullPath);
					AddRoute(node.Children, target, open);
					return;
				}
			}
		}

		#endregion

		private static string GetSignature(IEnumerable<HistoryNode> nodes)
			=> string.Join('|', nodes.Select(n => $"{n.FullPath}\u0001{n.Name}\u0001{(n.IsLeaf ? 1 : 0)}[{GetSignature(n.Children)}]"));

		private void ApplyTree(IReadOnlyList<string> pins, IReadOnlyList<HistoryNode> nodes)
		{
			var selectedPath = tree.SelectedNode?.Tag as string;

			building = true;
			tree.BeginUpdate();
			try
			{
				tree.Nodes.Clear();

				if (pins.Count > 0)
				{
					var group = new TreeNode("ピン留め") { Tag = PinGroupTag };
					foreach (var pin in pins)
						group.Nodes.Add(CreatePinnedNode(pin));
					tree.Nodes.Add(group);
				}

				if (nodes.Count == 0)
				{
					if (pins.Count == 0)
						tree.Nodes.Add("(まだ履歴がありません)");
				}
				else
				{
					tree.Nodes.AddRange(nodes.Select(CreateNode).ToArray());
				}

				foreach (TreeNode root in tree.Nodes)
					ExpandUnlessCollapsed(root);

				if (selectedPath is not null && FindNode(tree.Nodes, selectedPath) is { } found)
					tree.SelectedNode = found;
			}
			finally
			{
				tree.EndUpdate();
				building = false;
			}
		}

		private static TreeNode CreatePinnedNode(string path)
		{
			var name = Path.GetFileName(path.TrimEnd('\\', '/'));
			return new TreeNode(string.IsNullOrEmpty(name) ? path : name)
			{
				Tag = path,
				ToolTipText = path,
			};
		}

		private static TreeNode CreateNode(HistoryNode node)
		{
			var item = new TreeNode(node.Name)
			{
				Tag = node.FullPath,
				ToolTipText = $"{node.FullPath}{Environment.NewLine}最終使用: {node.LastUsed.ToLocalTime():yyyy-MM-dd HH:mm}",
			};

			foreach (var child in node.Children)
				item.Nodes.Add(CreateNode(child));

			return item;
		}

		private void ExpandUnlessCollapsed(TreeNode node)
		{
			if (node.Tag is string path && !collapsed.Contains(path))
				node.Expand();

			foreach (TreeNode child in node.Nodes)
				ExpandUnlessCollapsed(child);
		}

		private static TreeNode? FindNode(TreeNodeCollection nodes, string path)
		{
			foreach (TreeNode node in nodes)
			{
				if (node.Tag is string p && HistoryPath.Comparer.Equals(p, path))
					return node;

				if (FindNode(node.Nodes, path) is { } inner)
					return inner;
			}

			return null;
		}

		// A double click opens the folder, so it must not also fold the row.
		private void OnBeforeToggle(object? sender, TreeViewCancelEventArgs e)
		{
			if (suppressToggle && e.Action == TreeViewAction.ByMouse)
				e.Cancel = true;

			suppressToggle = false;
		}

		private void TrackExpansion(TreeNode? node, bool expanded)
		{
			if (building || node?.Tag is not string path)
				return;

			if (expanded)
				collapsed.Remove(path);
			else
				collapsed.Add(path);
		}

		// The path of a folder row; null for the pin group and the placeholder.
		private static string? GetPath(TreeNode? node)
			=> node?.Tag is string path && path != PinGroupTag ? path : null;

		private static bool IsPinned(TreeNode node)
			=> node.Parent?.Tag is string tag && tag == PinGroupTag;

		private void OpenInExplorer(TreeNode? node, bool forceNewWindow = false)
		{
			if (GetPath(node) is not { } path)
				return;

			if (!FolderCheck.Exists(path))
			{
				ShowMessage("フォルダが見つかりません。\n" + path, MessageBoxIcon.Information);
				ScheduleRebuild();
				return;
			}

			if (!forceNewWindow && settings.ReuseExplorer && ExplorerNavigator.TryNavigate(path))
				return;

			try
			{
				Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
			}
			catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
			{
				ShowMessage("エクスプローラーを開けませんでした。\n" + ex.Message, MessageBoxIcon.Warning);
			}
		}

		private void CopyPath(TreeNode? node)
		{
			if (GetPath(node) is not { } path)
				return;

			try
			{
				Clipboard.SetText(path);
			}
			catch (System.Runtime.InteropServices.ExternalException)
			{
				ShowMessage("クリップボードにコピーできませんでした。", MessageBoxIcon.Warning);
			}
		}

		// While a dialog is open the edge mode must not hide the window under it.
		private void ShowMessage(string text, MessageBoxIcon icon)
		{
			modalDepth++;
			try
			{
				MessageBox.Show(this, text, Text, MessageBoxButtons.OK, icon);
			}
			finally
			{
				modalDepth--;
			}
		}

		#region Pinned folders

		private void Pin(string path)
		{
			var normalized = HistoryPath.Normalize(path);
			if (settings.PinnedFolders.Any(p => HistoryPath.Comparer.Equals(p, normalized)))
				return;

			if (settings.PinnedFolders.Count >= MaxPinned)
			{
				ShowMessage($"ピン留めは {MaxPinned} 件までです。不要なものを外してから追加してください。", MessageBoxIcon.Information);
				return;
			}

			settings.PinnedFolders.Add(normalized);
			settings.Save();
			ScheduleRebuild();
		}

		private void Unpin(string path)
		{
			if (settings.PinnedFolders.RemoveAll(p => HistoryPath.Comparer.Equals(p, path)) > 0)
			{
				settings.Save();
				ScheduleRebuild();
			}
		}

		private void AddPinnedFolderFromDialog()
		{
			using var dialog = new FolderBrowserDialog
			{
				Description = "ピン留めするフォルダを選んでください",
				UseDescriptionForTitle = true,
			};

			modalDepth++;
			try
			{
				if (dialog.ShowDialog(this) == DialogResult.OK)
					Pin(dialog.SelectedPath);
			}
			finally
			{
				modalDepth--;
			}
		}

		private static void OnTreeDragOver(object? sender, DragEventArgs e)
			=> e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Link : DragDropEffects.None;

		// A dropped folder is pinned as it is; a dropped file pins the folder that holds it.
		private void OnTreeDrop(object? sender, DragEventArgs e)
		{
			if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths)
				return;

			foreach (var dropped in paths)
			{
				var folder = Directory.Exists(dropped) ? dropped : Path.GetDirectoryName(dropped);
				if (!string.IsNullOrEmpty(folder))
					Pin(folder);
			}
		}

		#endregion

		#region Left edge

		private void ApplyEdgeMode(bool on)
		{
			settings.EdgeMode = on;
			settings.Save();
			TopMost = settings.TopMost || on;

			if (on)
				ShowAtEdge();
		}

		private void PlaceAtEdge()
		{
			var area = Screen.AllScreens.OrderBy(screen => screen.Bounds.Left).First().WorkingArea;
			var width = Math.Clamp(settings.EdgeWidth, 240, Math.Max(240, area.Width / 2));
			Bounds = new Rectangle(area.Left, area.Top, width, area.Height);
		}

		private void ShowAtEdge()
		{
			PlaceAtEdge();
			ShowWindow();
		}

		// Polls the pointer: it shows the window after the pointer rests on the left edge, and hides it once the pointer has stayed away.
		private void OnEdgeTick(object? sender, EventArgs e)
		{
			if (!settings.EdgeMode)
				return;

			var pointer = Cursor.Position;

			if (!Visible)
			{
				var screen = Screen.AllScreens.OrderBy(s => s.Bounds.Left).First();
				var atEdge = pointer.X <= screen.Bounds.Left
					&& pointer.Y >= screen.WorkingArea.Top
					&& pointer.Y <= screen.WorkingArea.Bottom;

				edgeShowTicks = atEdge && MouseButtons == MouseButtons.None ? edgeShowTicks + 1 : 0;
				if (edgeShowTicks >= EdgeShowTicks)
				{
					edgeShowTicks = 0;
					ShowAtEdge();
				}

				return;
			}

			var near = Rectangle.Inflate(Bounds, 12, 12).Contains(pointer);
			if (near || menuOpen || modalDepth > 0 || MouseButtons != MouseButtons.None)
			{
				edgeHideTicks = 0;
				return;
			}

			if (++edgeHideTicks >= EdgeHideTicks)
			{
				edgeHideTicks = 0;
				SaveBounds();
				Hide();
			}
		}

		#endregion

		private async Task ShowDiagnosticsAsync()
		{
			try
			{
				var text = await Task.Run(() =>
				{
					var report = FolderSelector.SelectWithReport(log.Snapshot(), FolderCheck.Exists);
					var built = HistoryTreeBuilder.Build(report.Selected, BaseFolders);
					return HistoryDiagnostics.Format(log, report, built, DateTimeOffset.UtcNow);
				});

				Directory.CreateDirectory(AppPaths.DataDirectory);
				await File.WriteAllTextAsync(AppPaths.ReportFile, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
				Process.Start(new ProcessStartInfo(AppPaths.ReportFile) { UseShellExecute = true });
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
			{
				ShowMessage("診断レポートを表示できませんでした。\n" + ex.Message, MessageBoxIcon.Warning);
			}
		}

		private void ApplySavedBounds()
		{
			if (settings.HasBounds)
			{
				var saved = new Rectangle(settings.Left, settings.Top, settings.Width, settings.Height);
				if (Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(saved)))
				{
					Bounds = saved;
					return;
				}
			}

			Size = new Size(settings.Width, settings.Height);
			var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
			Location = new Point(Math.Max(area.Left, area.Right - Width - 20), area.Top + 60);
		}

		private void SaveBounds()
		{
			var bounds = WindowState == FormWindowState.Normal ? Bounds : base.RestoreBounds;

			if (settings.EdgeMode)
			{
				settings.EdgeWidth = bounds.Width;
				settings.Save();
				return;
			}

			settings.HasBounds = true;
			settings.Left = bounds.Left;
			settings.Top = bounds.Top;
			settings.Width = bounds.Width;
			settings.Height = bounds.Height;
			settings.Save();
		}

		private void ShowWindow()
		{
			if (!Visible)
			{
				if (settings.EdgeMode)
					PlaceAtEdge();

				Show();
			}

			if (WindowState == FormWindowState.Minimized)
				WindowState = FormWindowState.Normal;

			// Toggling TopMost lifts the window above the others even when Windows refuses to move focus.
			var topMost = TopMost;
			TopMost = true;
			TopMost = topMost;
			Activate();
		}

		private void ToggleWindow()
		{
			if (Visible && WindowState != FormWindowState.Minimized)
				Hide();
			else
				ShowWindow();
		}

		private void ExitApp()
		{
			exiting = true;
			Close();
		}
	}
}
