// Copyright (c) koji inoguchi(nagasaki)
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text;
using Files.History;

namespace FolderHistory
{
	internal sealed class MainForm : Form
	{
		private readonly EventWaitHandle activateSignal;
		private readonly bool startHidden;
		private readonly AppSettings settings = AppSettings.Load();
		private readonly HistoryLog log = new();
		private readonly HistoryStore store = new(AppPaths.HistoryFile);
		private readonly TreeView tree = new();
		private readonly ToolStripStatusLabel statusLabel = new();
		private readonly ToolStripButton topMostButton = new("常に手前") { CheckOnClick = true };
		private readonly ToolStripButton autoStartButton = new("自動起動") { CheckOnClick = true };
		private readonly NotifyIcon tray = new();
		private readonly System.Windows.Forms.Timer rebuildTimer = new() { Interval = 1000 };
		private readonly HashSet<string> collapsed = new(HistoryPath.Comparer);

		private RegisteredWaitHandle? activateWait;
		private RecentItemsSource? recentSource;
		private OfficeMruSource? wordSource;
		private string? signature;
		private bool visibleDecided;
		private bool initStarted;
		private bool building;
		private bool rebuilding;
		private bool rebuildAgain;
		private bool suppressToggle;
		private bool exiting;

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
			TopMost = settings.TopMost;

			ConfigureAutoStartOnFirstRun();
			BuildUi();
			BuildTray();

			// The app's own data folder holds this history and the report; opening them must not count as use.
			var dataFolder = HistoryPath.Normalize(AppPaths.DataDirectory);
			log.Exclude = path => HistoryPath.IsAncestor(dataFolder, path);

			rebuildTimer.Tick += OnRebuildTick;
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

			if (initStarted)
				return;

			initStarted = true;
			BeginInvoke((Action)(() => _ = InitializeAsync()));
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
				TopMost = topMostButton.Checked;
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
					MessageBox.Show(this, "自動起動の設定を変更できませんでした。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				}
			};

			var diagnosticsButton = new ToolStripButton("診断");
			diagnosticsButton.Click += async (_, _) => await ShowDiagnosticsAsync();

			toolbar.Items.AddRange(new ToolStripItem[]
			{
				refreshButton,
				new ToolStripSeparator(),
				topMostButton,
				autoStartButton,
				new ToolStripSeparator(),
				diagnosticsButton,
			});

			tree.Dock = DockStyle.Fill;
			tree.ShowNodeToolTips = true;
			tree.HideSelection = false;
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
			};

			var menu = new ContextMenuStrip();
			menu.Items.Add("エクスプローラーで開く", null, (_, _) => OpenInExplorer(tree.SelectedNode));
			menu.Items.Add("パスをコピー", null, (_, _) => CopyPath(tree.SelectedNode));
			menu.Opening += (_, e) => e.Cancel = tree.SelectedNode?.Tag is not string;
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
					var (nodes, newSignature, folderCount) = await Task.Run(() =>
					{
						var selected = FolderSelector.Select(events, FolderCheck.Exists);
						var built = HistoryTreeBuilder.Build(selected);
						return (built, GetSignature(built), selected.Count);
					});

					// Rebuilding resets the rows, so leave them alone when nothing changed.
					if (newSignature != signature)
					{
						signature = newSignature;
						ApplyTree(nodes);
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

		private static string GetSignature(IEnumerable<HistoryNode> nodes)
			=> string.Join('|', nodes.Select(n => $"{n.FullPath}\u0001{n.Name}\u0001{(n.IsLeaf ? 1 : 0)}[{GetSignature(n.Children)}]"));

		private void ApplyTree(IReadOnlyList<HistoryNode> nodes)
		{
			var selectedPath = tree.SelectedNode?.Tag as string;

			building = true;
			tree.BeginUpdate();
			try
			{
				tree.Nodes.Clear();

				if (nodes.Count == 0)
				{
					tree.Nodes.Add("(まだ履歴がありません)");
					return;
				}

				tree.Nodes.AddRange(nodes.Select(CreateNode).ToArray());

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

		private void OpenInExplorer(TreeNode? node)
		{
			if (node?.Tag is not string path)
				return;

			if (!FolderCheck.Exists(path))
			{
				MessageBox.Show(this, "フォルダが見つかりません。\n" + path, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
				ScheduleRebuild();
				return;
			}

			try
			{
				Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
			}
			catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
			{
				MessageBox.Show(this, "エクスプローラーを開けませんでした。\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		private void CopyPath(TreeNode? node)
		{
			if (node?.Tag is not string path)
				return;

			try
			{
				Clipboard.SetText(path);
			}
			catch (System.Runtime.InteropServices.ExternalException)
			{
				MessageBox.Show(this, "クリップボードにコピーできませんでした。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		private async Task ShowDiagnosticsAsync()
		{
			try
			{
				var text = await Task.Run(() =>
				{
					var report = FolderSelector.SelectWithReport(log.Snapshot(), FolderCheck.Exists);
					var built = HistoryTreeBuilder.Build(report.Selected);
					return HistoryDiagnostics.Format(log, report, built, DateTimeOffset.UtcNow);
				});

				Directory.CreateDirectory(AppPaths.DataDirectory);
				await File.WriteAllTextAsync(AppPaths.ReportFile, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
				Process.Start(new ProcessStartInfo(AppPaths.ReportFile) { UseShellExecute = true });
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
			{
				MessageBox.Show(this, "診断レポートを表示できませんでした。\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
				Show();

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
