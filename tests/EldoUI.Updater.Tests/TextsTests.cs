using EldoUI.Updater;
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
	}
}
