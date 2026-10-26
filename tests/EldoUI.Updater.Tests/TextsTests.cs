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
			Assert.EndsWith(moreEnding, AddonRows.Covered(AddonCatalog.Find("elvui")));
		}
	}
}
