// --selftest: the Linux parts in a temp folder, without the network: finding WoW in Wine, Lutris, Bottles
// and Steam prefixes, installing EldoUI into it, settings, the Extra addons rows, the setup assistant's
// system check, the updater's own requirement check, and the embedded icon. CI runs it on Linux.
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using EldoUI.Updater.Core;

namespace EldoUI.Updater
{
	static class SelfTest
	{
		public static int Run(TextWriter output)
		{
			string home = Path.Combine(Path.GetTempPath(), "eldoui-updater-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
			int failures = 0;
			void Check(bool ok, string what)
			{
				output.WriteLine((ok ? "ok    " : "FAIL  ") + what);
				if (!ok) failures++;
			}
			try
			{
				string lutris = Path.Combine(home, "Games", "battlenet");
				string beta = Path.Combine(lutris, "drive_c", "Program Files (x86)", "World of Warcraft", "_classic_beta_");
				Directory.CreateDirectory(beta);
				Check(WineFolders.Find(home) == Path.Combine(beta, "Interface", "AddOns"), "the beta is found in a Lutris prefix");
				string proton = Path.Combine(home, ".local", "share", "Steam", "steamapps", "compatdata", "1", "pfx");
				string forever = Path.Combine(proton, "drive_c", "Program Files (x86)", "World of Warcraft", "_forever_");
				Directory.CreateDirectory(forever);
				string addOns = Path.Combine(forever, "Interface", "AddOns");
				Check(WineFolders.Find(home) == addOns, "the released game in a Steam prefix wins over the beta");
				Check(WowFolders.ResolveAddOns(Path.Combine(proton, "drive_c", "Program Files (x86)", "World of Warcraft")) == addOns, "a picked WoW folder resolves to AddOns");

				string zip = Path.Combine(home, "EldoUI-0.7.0002.zip");
				using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
				{
					var e = z.CreateEntry("EldoUI/EldoUI.toc");
					using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write("## Interface: 16001\n## Version: 0.7.0002\n");
				}
				var r = AddonInstaller.Install(zip, addOns, null, "0.7.0002");
				Check(r.Version == "0.7.0002" && AddonInstaller.InstalledVersion(addOns) == "0.7.0002", "EldoUI installs into the prefix");
				bool wrongVersion = false;
				try { AddonInstaller.Install(zip, addOns, r.Folders, "0.7.0003"); }
				catch (InvalidDataException) { wrongVersion = true; }
				Check(wrongVersion && AddonInstaller.InstalledVersion(addOns) == "0.7.0002",
					"a zip for another release is rejected before replacing EldoUI");

				var s = new Settings { AddOnsDir = addOns };
				string settingsPath = Path.Combine(home, "settings.json");
				s.Save(settingsPath);
				Check(Settings.Load(settingsPath).AddOnsDir == addOns, "settings are saved and read back");
				var view = AddonRows.Sample(Path.Combine(home, "sample", "AddOns"));
				Check(AddonRows.Find(view, "", null).Count >= AddonCatalog.All.Count && AddonRows.Installed(view, false).Count > 0, "the Extra addons window has its catalog and the installed addons");
				string sampleAddOns = Path.Combine(home, "sample", "AddOns");
				var removal = AddonRemover.Plan(sampleAddOns, view.Installed, new[] { "OldMapNotes" });
				Check(removal != null && AddonRemover.Plan(sampleAddOns, view.Installed, new[] { "EldoUI" }) == null, "every installed addon but EldoUI can be uninstalled");
				AddonRemover.Remove(sampleAddOns, removal, false);
				Check(!Directory.Exists(Path.Combine(sampleAddOns, "OldMapNotes")) && Directory.Exists(Path.Combine(sampleAddOns, "EldoUI")), "an uninstall removes only that addon");

				var system = LightforgeChecks.Evaluate(SystemInfo.Read(home));
				var glibc = system.Find(c => c.Key == "lf.check.glibc");
				Check(system.Count == 7 && glibc != null && !string.IsNullOrEmpty(glibc.Found), "the setup assistant reads this system (glibc " + (glibc != null ? glibc.Found : "?") + ")");
				var plan = LightforgePlan.For(home, null);
				Check(plan.Cli == Path.Combine(home, ".local", "bin", "lightforge") && plan.Desktop.StartsWith(Path.Combine(home, ".local", "share", "applications")), "Lightforge's files go only into the home folder");

				var probe = Requirements.Read();
				Check(Requirements.Report(probe, Setup.Version).Length > 0, "the requirement check reads this system (" + (probe.Name ?? "unknown") + ")");
				Check(Requirements.DesktopExec("/home/x/a b.AppImage", "--tray", true) == "env APPIMAGE_EXTRACT_AND_RUN=1 \"/home/x/a b.AppImage\" --tray",
					"without FUSE the app menu and login entries start the updater unpacked");

				using (var logo = Assets.Open("logo.png")) Check(logo.Length > 1000, "the icon is in the program");
				Check(!string.IsNullOrEmpty(Setup.Version), "the version is " + Setup.Version);
			}
			catch (Exception ex)
			{
				Check(false, "unexpected error: " + ex);
			}
			finally
			{
				try { Directory.Delete(home, true); } catch { }
			}
			output.WriteLine(failures == 0 ? "selftest passed" : failures + " selftest check(s) failed");
			return failures == 0 ? 0 : 1;
		}
	}
}
