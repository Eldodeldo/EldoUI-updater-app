using EldoUI.Updater;
using EldoUI.Updater.Core;
using Xunit;

namespace EldoUI.Updater.Tests
{
	public class TextsTests
	{
		[Theory]
		[InlineData("en", "details", "try again")]
		[InlineData("da", "detaljer", "prøv igen")]
		public void FailedCheckExplainsDetailsAndRetry(string language, string details, string retry)
		{
			T.Use(language);
			Assert.Contains(details, T.S("state.failed"));
			Assert.Contains(retry, T.S("state.failed"));
		}

		[Theory]
		[InlineData("en", "EldoUI already does this: Nameplates.", " more.")]
		[InlineData("da", "EldoUI gør det allerede: Nameplates.", " mere.")]
		public void CoveredAddonsNameTheModule(string language, string plater, string moreEnding)
		{
			T.Use(language);
			Assert.Equal(plater, AddonRows.Covered(AddonCatalog.Find("plater")));
			Assert.EndsWith(moreEnding, AddonRows.Covered(AddonCatalog.Find("elvui")).Split('\n')[0]);
		}

		[Theory]
		[InlineData("en", "EldoUI already does this: X.\nEldoUI does part of this: Y, Z.\nWorks with EldoUI: W.")]
		[InlineData("da", "EldoUI gør det allerede: X.\nEldoUI gør en del af det: Y, Z.\nVirker med EldoUI: W.")]
		public void TheNoteHasALineForEachKind(string language, string expected)
		{
			T.Use(language);
			var entry = new CatalogEntry { Id = "x" };
			entry.CoveredBy.Add(new CoveringModule { Module = "W", Scope = "works", English = "W", Danish = "W" });
			entry.CoveredBy.Add(new CoveringModule { Module = "Y", Scope = "part", English = "Y", Danish = "Y" });
			entry.CoveredBy.Add(new CoveringModule { Module = "X", Scope = "full", English = "X", Danish = "X" });
			entry.CoveredBy.Add(new CoveringModule { Module = "Z", Scope = "part", English = "Z", Danish = "Z" });
			Assert.Equal(expected, AddonRows.Covered(entry));
		}
	}
}
