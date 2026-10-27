using System.Linq;
using EldoUI.Updater;
using Xunit;

namespace EldoUI.Updater.Tests
{
	public class RequirementsTests
	{
		public RequirementsTests() { LinuxTexts.Apply(); }

		static LinuxSystem Desktop(string osRelease)
		{
			var s = new LinuxSystem { Display = true, Fontconfig = true, X11 = true, Fuse = true };
			Requirements.ReadOsRelease(s, osRelease);
			return s;
		}

		const string Ubuntu = "PRETTY_NAME=\"Ubuntu 24.04.5 LTS\"\nNAME=\"Ubuntu\"\nVERSION_ID=\"24.04\"\nID=ubuntu\nID_LIKE=debian\n";
		const string Mint = "NAME=\"Linux Mint\"\nVERSION_ID=\"22.1\"\nID=linuxmint\nID_LIKE=\"ubuntu debian\"\nPRETTY_NAME=\"Linux Mint 22.1\"\n";
		const string SteamOS = "NAME=\"SteamOS\"\nPRETTY_NAME=\"SteamOS\"\nVERSION_ID=3.7.13\nID=steamos\nID_LIKE=arch\n";
		const string Leap = "NAME=\"openSUSE Leap\"\nVERSION_ID=\"15.6\"\nID=\"opensuse-leap\"\nID_LIKE=\"suse opensuse\"\nPRETTY_NAME=\"openSUSE Leap 15.6\"\n";
		const string Fedora = "NAME=\"Fedora Linux\"\nVERSION_ID=44\nID=fedora\nPRETTY_NAME=\"Fedora Linux 44 (Workstation Edition)\"\n";
		const string Arch = "NAME=\"Arch Linux\"\nPRETTY_NAME=\"Arch Linux\"\nID=arch\nBUILD_ID=rolling\n";

		[Fact]
		public void OsReleaseIsRead()
		{
			var s = Desktop(Mint);
			Assert.Equal("linuxmint", s.Id);
			Assert.Equal(new[] { "ubuntu", "debian" }, s.IdLike);
			Assert.Equal("22.1", s.VersionId);
			Assert.Equal("Linux Mint 22.1", s.Name);
		}

		[Fact]
		public void ADesktopHasEverything()
		{
			var s = Desktop(Ubuntu);
			Assert.Empty(Requirements.Missing(s));
			Assert.True(Requirements.CanRun(s));
		}

		[Fact]
		public void MissingFuseDoesNotStopTheUpdater()
		{
			var s = Desktop(Ubuntu);
			s.Fuse = false;
			Assert.Equal(new[] { Need.Fuse }, Requirements.Missing(s));
			Assert.True(Requirements.CanRun(s));
			Assert.Contains("sudo apt install fuse3", Requirements.Report(s, "1.2.11"));
		}

		[Fact]
		public void MissingLibrariesOrSessionStopItBeforeItInstalls()
		{
			foreach (Need n in new[] { Need.Display, Need.Fontconfig, Need.X11 })
			{
				var s = Desktop(Ubuntu);
				if (n == Need.Display) s.Display = false;
				if (n == Need.Fontconfig) s.Fontconfig = false;
				if (n == Need.X11) s.X11 = false;
				Assert.False(Requirements.CanRun(s), n.ToString());
			}
		}

		[Theory]
		[InlineData(Ubuntu, "debian", "sudo apt install libfontconfig1")]
		[InlineData(Mint, "debian", "sudo apt install libfontconfig1")]
		[InlineData(Fedora, "fedora", "sudo dnf install fontconfig")]
		[InlineData(Arch, "arch", "sudo pacman -S fontconfig")]
		[InlineData(SteamOS, "arch", "sudo pacman -S fontconfig")]
		[InlineData(Leap, "suse", "sudo zypper install fontconfig")]
		public void TheFixNamesThePackageOfThisDistribution(string osRelease, string family, string command)
		{
			var s = Desktop(osRelease);
			s.Fontconfig = false;
			Assert.Equal(family, Requirements.Family(s));
			Assert.Contains(command, Requirements.Report(s, "1.2.11"));
		}

		[Fact]
		public void AnUnknownDistributionGetsThePackageName()
		{
			var s = Desktop("ID=somethingelse\nPRETTY_NAME=\"Something Else 1\"\n");
			s.X11 = false;
			Assert.Null(Requirements.Family(s));
			Assert.Null(Requirements.InstallCommand(Need.X11, null));
			Assert.Contains("libx11", Requirements.Report(s, "1.2.11"));
		}

		[Fact]
		public void WaylandWithoutXWaylandIsNamed()
		{
			var s = Desktop(Fedora);
			s.Display = false;
			s.Wayland = true;
			Assert.Equal(T.S("req.wayland"), Requirements.Advice(Need.Display, s));
		}

		[Theory]
		[InlineData(Ubuntu, true)]
		[InlineData(Mint, true)]
		[InlineData(Fedora, true)]
		[InlineData(Arch, true)]
		[InlineData(Leap, false)]
		[InlineData(SteamOS, false)]
		public void TheTestedListIsMatchedByIdAndVersion(string osRelease, bool tested)
		{
			Assert.Equal(tested, Requirements.Match(Desktop(osRelease)) != null);
		}

		[Fact]
		public void TheTestedListNamesEveryEntry()
		{
			string list = Requirements.TestedList();
			Assert.All(Requirements.Tested, t => Assert.Contains(t.Name, list));
			Assert.Equal(Requirements.Tested.Length, Requirements.Tested.Select(t => t.Id).Distinct().Count());
		}

		[Fact]
		public void EntriesStartAnUnpackedCopyWithoutFuse()
		{
			Assert.Equal("\"/home/p/x.AppImage\" --tray", Requirements.DesktopExec("/home/p/x.AppImage", "--tray", false));
			Assert.Equal("env APPIMAGE_EXTRACT_AND_RUN=1 \"/home/p/x.AppImage\"", Requirements.DesktopExec("/home/p/x.AppImage", "", true));
			Assert.Equal("\"/home/p/a\\\"b.AppImage\"", Requirements.DesktopExec("/home/p/a\"b.AppImage", null, false));
		}
	}
}
