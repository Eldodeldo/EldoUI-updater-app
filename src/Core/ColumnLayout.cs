// The size of the Extra addons window and the widths of its list columns. Columns are kept as shares of the
// list's width, so they grow and shrink with the window, and dragging an edge moves width between the two
// columns beside it. Sizes are in logical pixels (96 per inch), so they mean the same at any display scale.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EldoUI.Updater.Core
{
	public sealed class WindowLayout
	{
		/// <summary>The window's inner size the player left it at; 0 for the default size.</summary>
		public int Width, Height;
		/// <summary>Column shares per list ("find", "installed").</summary>
		public Dictionary<string, List<double>> Columns = new Dictionary<string, List<double>>();

		public Dictionary<string, object> ToJson()
		{
			return new Dictionary<string, object>
			{
				{ "width", Width },
				{ "height", Height },
				{ "columns", Columns.ToDictionary(p => p.Key, p => (object)p.Value.Select(v => (object)Math.Round(v, 4)).ToList()) },
			};
		}

		public static WindowLayout FromJson(Dictionary<string, object> o)
		{
			var layout = new WindowLayout();
			if (o == null) return layout;
			layout.Width = (int)Math.Max(0, Math.Min(10000, o.Num("width")));
			layout.Height = (int)Math.Max(0, Math.Min(10000, o.Num("height")));
			foreach (var item in o.Obj("columns") ?? new Dictionary<string, object>())
				if (item.Value is List<object>)
					layout.Columns[item.Key] = ((List<object>)item.Value).OfType<double>().ToList();
			return layout;
		}
	}

	public static class ColumnLayout
	{
		/// <summary>The narrowest a column can be dragged, in logical pixels.</summary>
		public const int MinWidth = 56;

		/// <summary>The shares a list starts with: name, kind, source or version, status.</summary>
		public static double[] Default(string list)
		{
			return list == "installed" ? new[] { 0.34, 0.2, 0.14, 0.32 } : new[] { 0.3, 0.17, 0.15, 0.38 };
		}

		/// <summary>Saved shares when they are usable (right count, all positive), otherwise the defaults.</summary>
		public static double[] Shares(WindowLayout layout, string list)
		{
			var fallback = Default(list);
			List<double> saved;
			if (layout == null || !layout.Columns.TryGetValue(list, out saved) || saved.Count != fallback.Length || saved.Any(v => !(v > 0) || double.IsInfinity(v)))
				return fallback;
			double sum = saved.Sum();
			return saved.Select(v => v / sum).ToArray();
		}

		/// <summary>Pixel widths that fill <paramref name="total"/> exactly, each at least <paramref name="min"/> when there is room.</summary>
		public static int[] Widths(double[] shares, int total, int min)
		{
			int n = shares.Length;
			var widths = new int[n];
			if (n == 0) return widths;
			total = Math.Max(0, total);
			min = Math.Min(min, total / n);
			var exact = shares.Select(v => v * total).ToArray();
			double deficit = 0, excess = 0;
			for (int i = 0; i < n; i++)
				if (exact[i] < min) { deficit += min - exact[i]; exact[i] = min; }
			for (int i = 0; i < n; i++)
				if (exact[i] > min) excess += exact[i] - min;
			if (deficit > 0 && excess > 0)
				for (int i = 0; i < n; i++)
					if (exact[i] > min) exact[i] -= deficit * (exact[i] - min) / excess;
			int used = 0;
			for (int i = 0; i < n; i++)
			{
				widths[i] = (int)Math.Round(exact[i]);
				used += widths[i];
			}
			widths[n - 1] += total - used;
			return widths;
		}

		/// <summary>New shares after the player drags the edge between column <paramref name="edge"/> and the next one by <paramref name="delta"/> pixels.</summary>
		public static double[] Drag(double[] shares, int edge, int delta, int total, int min)
		{
			if (edge < 0 || edge >= shares.Length - 1 || total <= 0) return shares;
			var widths = Widths(shares, total, min);
			int pair = widths[edge] + widths[edge + 1];
			int floor = Math.Min(min, pair / 2);
			int left = Math.Max(floor, Math.Min(pair - floor, widths[edge] + delta));
			widths[edge] = left;
			widths[edge + 1] = pair - left;
			return widths.Select(w => (double)w / total).ToArray();
		}
	}
}
