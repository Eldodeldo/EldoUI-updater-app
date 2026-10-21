// The check when the app starts: anything new (EldoUI or the updater itself) installs at once and quietly,
// with no notice, and then the app runs as usual. While GitHub cannot be reached it tries again every minute,
// for at most ten minutes; after that the normal checks take over.
using System;

namespace EldoUI.Updater.Core
{
	public sealed class BootCheck
	{
		public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
		public static readonly TimeSpan RetryEvery = TimeSpan.FromMinutes(1);

		readonly DateTime _untilUtc;
		bool _done;

		public BootCheck(DateTime startedUtc)
		{
			_untilUtc = startedUtc + Window;
		}

		/// <summary>True until a check at start has reached GitHub, or the window has passed.</summary>
		public bool Active(DateTime nowUtc)
		{
			return !_done && nowUtc < _untilUtc;
		}

		/// <summary>The check at start reached GitHub: from here the normal checks and the 24-hour rule apply.</summary>
		public void Finish()
		{
			_done = true;
		}
	}
}
