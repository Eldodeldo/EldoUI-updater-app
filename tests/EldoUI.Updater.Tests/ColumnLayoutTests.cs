using System.Collections.Generic;
using System.IO;
using System.Linq;
using EldoUI.Updater.Core;
using Xunit;

namespace EldoUI.Updater.Tests
{
	public class ColumnLayoutTests
	{
		[Theory]
		[InlineData(400)]
		[InlineData(641)]
		[InlineData(1000)]
		public void WidthsFillTheListAndFollowTheShares(int total)
		{
			var shares = ColumnLayout.Default("find");
			var widths = ColumnLayout.Widths(shares, total, ColumnLayout.MinWidth);
			Assert.Equal(total, widths.Sum());
			Assert.All(widths, w => Assert.True(w >= ColumnLayout.MinWidth));
			Assert.True(widths[3] > widths[2]);
		}

		[Fact]
		public void ColumnsGrowWithTheWindow()
		{
			var shares = ColumnLayout.Default("installed");
			var narrow = ColumnLayout.Widths(shares, 500, ColumnLayout.MinWidth);
			var wide = ColumnLayout.Widths(shares, 1000, ColumnLayout.MinWidth);
			for (int i = 0; i < shares.Length; i++) Assert.True(wide[i] > narrow[i]);
		}

		[Fact]
		public void ANarrowColumnKeepsItsMinimum()
		{
			var widths = ColumnLayout.Widths(new[] { 0.94, 0.02, 0.02, 0.02 }, 600, 56);
			Assert.Equal(600, widths.Sum());
			Assert.All(widths, w => Assert.True(w >= 56));
		}

		[Fact]
		public void DraggingAnEdgeMovesWidthBetweenItsTwoColumns()
		{
			var shares = ColumnLayout.Default("find");
			var before = ColumnLayout.Widths(shares, 800, 56);
			var after = ColumnLayout.Widths(ColumnLayout.Drag(shares, 0, 60, 800, 56), 800, 56);
			Assert.Equal(before[0] + 60, after[0]);
			Assert.Equal(before[1] - 60, after[1]);
			Assert.Equal(before[2], after[2]);
			Assert.Equal(before[3], after[3]);

			var squeezed = ColumnLayout.Widths(ColumnLayout.Drag(shares, 0, 5000, 800, 56), 800, 56);
			Assert.Equal(56, squeezed[1]);
			Assert.Equal(before[0] + before[1] - 56, squeezed[0]);
			Assert.Same(shares, ColumnLayout.Drag(shares, 3, 10, 800, 56));
		}

		[Fact]
		public void BrokenSavedColumnsFallBackToTheDefaults()
		{
			var layout = new WindowLayout();
			layout.Columns["find"] = new List<double> { 0.5, 0.5 };
			layout.Columns["installed"] = new List<double> { 1, 1, 0, 1 };
			Assert.Equal(ColumnLayout.Default("find"), ColumnLayout.Shares(layout, "find"));
			Assert.Equal(ColumnLayout.Default("installed"), ColumnLayout.Shares(layout, "installed"));
			layout.Columns["find"] = new List<double> { 2, 1, 1, 4 };
			Assert.Equal(new[] { 0.25, 0.125, 0.125, 0.5 }, ColumnLayout.Shares(layout, "find"));
		}

		[Fact]
		public void TheWindowSizeAndColumnsAreSaved()
		{
			using (var t = new TempDir())
			{
				string path = t.Sub("settings.json");
				var s = new Settings();
				s.AddonsWindow.Width = 1100;
				s.AddonsWindow.Height = 760;
				s.AddonsWindow.Columns["find"] = new List<double> { 0.4, 0.2, 0.1, 0.3 };
				s.Save(path);
				var back = Settings.Load(path);
				Assert.Equal(1100, back.AddonsWindow.Width);
				Assert.Equal(760, back.AddonsWindow.Height);
				Assert.Equal(new[] { 0.4, 0.2, 0.1, 0.3 }, back.AddonsWindow.Columns["find"]);
				Assert.Equal(0, Settings.Load(t.Sub("missing.json")).AddonsWindow.Width);
			}
		}

		[Fact]
		public void SortingKeepsSuggestionsOnTopAndEmptyCellsLast()
		{
			var rows = new List<AddonRow>
			{
				new AddonRow { Key = "b", Title = "Beta", Extra = "1.10" },
				new AddonRow { Key = "suggest:x", Title = "Needed" },
				new AddonRow { Key = "a", Title = "alpha", Extra = "1.9" },
				new AddonRow { Key = "c", Title = "Gamma", Extra = "" },
			};
			Assert.Equal(new[] { "suggest:x", "a", "b", "c" }, AddonRows.Sort(rows, 0, false).Select(r => r.Key));
			Assert.Equal(new[] { "suggest:x", "c", "b", "a" }, AddonRows.Sort(rows, 0, true).Select(r => r.Key));
			Assert.Equal(new[] { "suggest:x", "a", "b", "c" }, AddonRows.Sort(rows, 2, false).Select(r => r.Key));
			Assert.Equal(new[] { "suggest:x", "b", "a", "c" }, AddonRows.Sort(rows, 2, true).Select(r => r.Key));
		}

		[Fact]
		public void EveryListHasFourHeadings()
		{
			Assert.Equal(4, AddonRows.Headers(false).Length);
			Assert.Equal(4, AddonRows.Headers(true).Length);
			Assert.Equal(ColumnLayout.Default("find").Length, AddonRows.Headers(false).Length);
		}
	}
}
