// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Text;

namespace Files.History
{
	/// <summary>
	/// Plain-text report explaining what the history holds and why each folder was or was not shown.
	/// </summary>
	public static class HistoryDiagnostics
	{
		public static string Format(HistoryLog log, SelectionReport report, IReadOnlyList<HistoryNode> tree, DateTimeOffset now)
		{
			var events = log.Snapshot().OrderByDescending(e => e.Time).ToList();
			var sb = new StringBuilder();

			sb.AppendLine("=== 履歴の診断 ===");
			sb.AppendLine($"作成: {Local(now)}");
			sb.AppendLine($"保持イベント: {events.Count} 件（集計対象は新しい順に最大 {HistoryDefaults.HistorySize} 件）");
			foreach (var source in Enum.GetValues<HistorySource>())
			{
				var last = log.GetLastImported(source);
				sb.AppendLine($"  {source}: {events.Count(e => e.Source == source)} 件 / 最終取り込み: {(last is null ? "なし" : Local(last.Value))}");
			}

			sb.AppendLine();
			sb.AppendLine($"=== 候補フォルダ（判定した順） 表示上限 {HistoryDefaults.MaxDisplay} 件 ===");
			var n = 0;
			foreach (var c in report.Candidates)
			{
				n++;
				var origin = c.Origin == CandidateOrigin.Recent ? "直近" : "頻度";
				sb.AppendLine($"[{n}] {c.Path}");
				sb.AppendLine($"     枠: {origin} / 使用 {c.Count} 回 / 最終 {Local(c.LastUsed)}");
				sb.AppendLine($"     結果: {Describe(c)}");
			}

			sb.AppendLine();
			sb.AppendLine("=== 表示ツリー（分岐点を根として表示） ===");
			if (tree.Count == 0)
				sb.AppendLine("(なし)");
			else
				WriteTree(sb, tree, 0);

			sb.AppendLine();
			sb.AppendLine("=== 直近のイベント（新しい順 40件） ===");
			foreach (var e in events.Take(40))
				sb.AppendLine($"{Local(e.Time)}  {e.Source,-11}  {e.FilePath}");

			return sb.ToString();
		}

		private static string Describe(CandidateReport c) => c.Outcome switch
		{
			CandidateOutcome.Selected => "表示対象にした",
			CandidateOutcome.SelectedReplacingAncestors => $"表示対象にした（上位フォルダ {c.Detail} は、これに置き換えて非表示）",
			CandidateOutcome.ReplacedByDescendant => $"一度は選んだが、配下の {c.Detail} が選ばれたため非表示",
			CandidateOutcome.SkippedHasSelectedDescendant => $"除外: 配下の {c.Detail} がすでに表示対象",
			CandidateOutcome.SkippedMissing => "除外: フォルダが存在しない",
			CandidateOutcome.NotEvaluated => $"未判定: 表示上限 {HistoryDefaults.MaxDisplay} 件に達したため",
			_ => c.Outcome.ToString()
		};

		private static void WriteTree(StringBuilder sb, IEnumerable<HistoryNode> nodes, int depth)
		{
			foreach (var node in nodes)
			{
				var kind = node.IsLeaf ? "孫" : "分岐";
				sb.AppendLine($"{new string(' ', depth * 2)}{node.Name}  [{kind}] {node.FullPath}  最終 {Local(node.LastUsed)}");
				WriteTree(sb, node.Children, depth + 1);
			}
		}

		private static string Local(DateTimeOffset t) => t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
	}
}
