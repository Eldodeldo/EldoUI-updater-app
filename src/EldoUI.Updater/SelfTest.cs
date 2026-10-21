// --selftest: the install path on the real .NET Framework, in a temp folder, without the network.
// CI runs it on Windows; the unit tests cover the same code in more detail.
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
			string root = Path.Combine(Path.GetTempPath(), "eldoui-updater-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
			int failures = 0;
			Action<bool, string> check = (ok, what) =>
			{
				output.WriteLine((ok ? "ok    " : "FAIL  ") + what);
				if (!ok) failures++;
			};
			try
			{
				string addOns = Path.Combine(root, "World of Warcraft", "_classic_beta_", "Interface", "AddOns");
				Directory.CreateDirectory(addOns);
				Directory.CreateDirectory(Path.Combine(addOns, "SomeOtherAddon"));
				File.WriteAllText(Path.Combine(addOns, "SomeOtherAddon", "SomeOtherAddon.toc"), "## Version: 1");

				check(WowFolders.ResolveAddOns(Path.Combine(root, "World of Warcraft")) == addOns, "the install folder resolves to Forever's AddOns");
				check(WowFolders.Find(new[] { Path.Combine(root, "World of Warcraft", "_retail_") }) == null, "a missing install is not found");
				Directory.CreateDirectory(Path.Combine(root, "World of Warcraft", "_retail_"));
				check(WowFolders.Find(new[] { Path.Combine(root, "World of Warcraft", "_retail_") }) == addOns, "the registry's _retail_ path leads to Forever");

				string zip1 = MakeZip(root, "0.7.0001", "EldoUI", "EldoUI_LootData", "EldoUI_Old");
				var r1 = AddonInstaller.Install(zip1, addOns, null);
				check(r1.Version == "0.7.0001" && AddonInstaller.InstalledVersion(addOns) == "0.7.0001", "first install");
				string zip2 = MakeZip(root, "0.7.0002", "EldoUI", "EldoUI_LootData");
				var r2 = AddonInstaller.Install(zip2, addOns, r1.Folders);
				check(AddonInstaller.InstalledVersion(addOns) == "0.7.0002", "update installs the new version");
				check(!Directory.Exists(Path.Combine(addOns, "EldoUI_Old")) && r2.Removed.Contains("EldoUI_Old"), "a folder the release dropped is removed");
				check(Directory.Exists(Path.Combine(addOns, "SomeOtherAddon")), "other addons are untouched");
				check(Directory.GetDirectories(Path.GetDirectoryName(addOns), ".eldoui-updater-*").Length == 0, "no work folder is left behind");

				string bad = Path.Combine(root, "bad.zip");
				using (var z = ZipFile.Open(bad, ZipArchiveMode.Create))
					Add(z, "Evil/Evil.toc", "## Version: 1");
				bool refused = false;
				try { AddonInstaller.Install(bad, addOns, r2.Folders); } catch (InvalidDataException) { refused = true; }
				check(refused && AddonInstaller.InstalledVersion(addOns) == "0.7.0002", "a zip that is not EldoUI is refused and nothing changes");

				var rel = Release.FromGitHub("{\"tag_name\":\"v0.7.0002\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"EldoUI-0.7.0002.zip\",\"size\":10,\"browser_download_url\":\"https://github.com/Eldodeldo/EldoUI/releases/download/v0.7.0002/EldoUI-0.7.0002.zip\",\"digest\":\"sha256:" + new string('a', 64) + "\"}]}");
				check(rel.Version == "0.7.0002" && rel.Find(UpdateEngine.AddonAssetPattern) != null, "GitHub's release answer is read");
				check(Versions.IsNewer("0.7.0010", "0.7.0002") && !Versions.IsNewer("0.7.0002", "v0.7.0002"), "versions compare");

				var s = new Settings { AddOnsDir = addOns };
				s.InstalledFolders.Add("EldoUI");
				string settingsPath = Path.Combine(root, "settings.json");
				s.Save(settingsPath);
				s.Save(settingsPath);
				var back = Settings.Load(settingsPath);
				check(back.AddOnsDir == addOns && back.InstalledFolders.Count == 1, "settings are saved and read back");
				check(Assets.Icon() != null && Assets.Logo() != null, "the icon and logo are in the exe");
				// Reads the .exe.config (TLS and DPI settings), so a broken config fails here and not on a player's PC.
				using (new Web("EldoUI-Updater/selftest")) { }
				check(System.Configuration.ConfigurationManager.GetSection("System.Windows.Forms.ApplicationConfigurationSection") != null
					|| !File.Exists(AppDomain.CurrentDomain.SetupInformation.ConfigurationFile), "the .exe.config is read");
			}
			catch (Exception ex)
			{
				check(false, "unexpected error: " + ex);
			}
			finally
			{
				try { Directory.Delete(root, true); } catch { }
			}
			output.WriteLine(failures == 0 ? "selftest passed" : failures + " selftest check(s) failed");
			return failures == 0 ? 0 : 1;
		}

		static string MakeZip(string dir, string version, params string[] folders)
		{
			string path = Path.Combine(dir, "EldoUI-" + version + "-" + folders.Length + ".zip");
			using (var z = ZipFile.Open(path, ZipArchiveMode.Create))
			{
				foreach (string f in folders)
				{
					Add(z, f + "/" + f + ".toc", "## Interface: 16001\n## Version: " + version + "\n");
					Add(z, f + "/Core/init.lua", "-- " + f);
				}
			}
			return path;
		}

		static void Add(ZipArchive z, string name, string text)
		{
			var e = z.CreateEntry(name);
			using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false)))
				w.Write(text);
		}
	}
}
