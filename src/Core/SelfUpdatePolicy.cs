// When the updater replaces itself: a new version is shown quietly in the window and menu (never as a
// notice) and installs by itself 24 hours after it was first seen, or at once when the player chooses.
using System;

namespace EldoUI.Updater.Core
{
	public static class SelfUpdatePolicy
	{
		public static readonly TimeSpan Grace = TimeSpan.FromHours(24);

		/// <summary>Notes a newer updater and returns when it installs by itself. The wait starts once per version, also across restarts.</summary>
		public static DateTime Note(Settings s, string version, DateTime nowUtc)
		{
			if (s.UpdaterSeenVersion != version || !s.UpdaterSeenAt.HasValue)
			{
				s.UpdaterSeenVersion = version;
				s.UpdaterSeenAt = nowUtc;
			}
			return s.UpdaterSeenAt.Value.ToUniversalTime() + Grace;
		}

		/// <summary>Forgets a noted version (it was installed, or GitHub no longer offers it). Returns true when something changed.</summary>
		public static bool Clear(Settings s)
		{
			if (string.IsNullOrEmpty(s.UpdaterSeenVersion) && !s.UpdaterSeenAt.HasValue) return false;
			s.UpdaterSeenVersion = "";
			s.UpdaterSeenAt = null;
			return true;
		}

		/// <summary>True when the wait is over.</summary>
		public static bool Due(DateTime installAtUtc, DateTime nowUtc)
		{
			return nowUtc >= installAtUtc;
		}
	}
}
