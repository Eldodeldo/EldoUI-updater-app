#!/usr/bin/env python3
"""Builds src/Core/CoverageData.cs: which catalog addons an EldoUI module replaces one to one.

Usage: python3 tools/build_coverage.py <EldoUI source folder> [EldoUI version]

EldoUI keeps its own list of other addons that do the same job as one of its modules ("replace"
entries: a module's `conflicts` list, and the whole-module rows in Data/Competitors.lua). This script
reads that list from EldoUI's source, matches each addon folder to the catalog, and writes the module
names in English and Danish (from EldoUI's own Danish texts). Entries that cover only part of a module
("overlap") are left out: the note is only for a direct replacement.

Run it again for every EldoUI release, and review the diff with the release.
"""
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def read(path):
	with open(path, encoding="utf-8") as f:
		return f.read()


def lua_files(folder):
	for base, _, files in os.walk(folder):
		for name in sorted(files):
			if name.endswith(".lua"):
				yield os.path.join(base, name)


def strings(text):
	return re.findall(r'"((?:[^"\\]|\\.)*)"', text)


def modules(src):
	"""Each module's title and its `conflicts` list, from E:NewModule("Name", { ... })."""
	found = {}
	for path in lua_files(os.path.join(src, "Modules")):
		text = read(path)
		starts = [m for m in re.finditer(r'NewModule\("([A-Za-z0-9_]+)",\s*\{', text)]
		for i, m in enumerate(starts):
			end = starts[i + 1].start() if i + 1 < len(starts) else len(text)
			body = text[m.end():min(end, m.end() + 4000)]
			title = re.search(r'^\s*title\s*=\s*(?:L\[)?"([^"]+)"', body, re.M)
			conflicts = re.search(r'^\s*conflicts\s*=\s*\{([^}]*)\}', body, re.M)
			found[m.group(1)] = {
				"title": title.group(1) if title else m.group(1),
				"conflicts": strings(conflicts.group(1)) if conflicts else [],
			}
	return found


def table_rows(text, name):
	"""The rows of `local NAME = { { "a", "b", ... }, ... }`."""
	m = re.search(r'local ' + name + r' = \{\n(.*?)\n\}', text, re.S)
	if not m:
		raise SystemExit("Data/Competitors.lua: table " + name + " not found")
	return [strings(row) for row in re.findall(r'^\s*\{([^{}]*)\},?\s*$', m.group(1), re.M)]


def competitors(src):
	"""Whole-module ("replace") rows from Data/Competitors.lua: (addon folder, module)."""
	text = read(os.path.join(src, "Data", "Competitors.lua"))
	pairs = []
	# for _, row in ipairs(NAME) do Add({ addon = row[1], module = row[2], scope = "replace" ...
	for name in re.findall(r'for _, row in ipairs\((\w+)\) do\s*\n\s*Add\(\{ addon = row\[1\], module = row\[2\], scope = "replace"', text):
		pairs += [(r[0], r[1]) for r in table_rows(text, name)]
	# for module, read in pairs(NAME) do Add({ addon = "X", module = module, scope = "replace" ...
	for name, addon in re.findall(r'for module, \w+ in pairs\((\w+)\) do\s*\n\s*Add\(\{ addon = "([^"]+)", module = module, scope = "replace"', text):
		m = re.search(r'local ' + name + r' = \{\n(.*?)\n\}', text, re.S)
		pairs += [(addon, key) for key in re.findall(r'^\s*(\w+) = function', m.group(1), re.M)]
	# Single Add({ addon = "X", module = "Y", scope = "replace" ... }) lines.
	pairs += re.findall(r'Add\(\{ addon = "([^"]+)", module = "([^"]+)", scope = "replace"', text)
	if not pairs:
		raise SystemExit("Data/Competitors.lua: no whole-module entries found")
	return pairs


def danish(src):
	words = {}
	folder = os.path.join(src, "EldoUI_Locale_daDK")
	for path in lua_files(folder):
		for key, value in re.findall(r'^\s*\["((?:[^"\\]|\\.)*)"\]\s*=\s*"((?:[^"\\]|\\.)*)",?\s*$', read(path), re.M):
			words.setdefault(key, value)
	return words


def catalog():
	text = read(os.path.join(ROOT, "src", "Core", "CatalogData.cs"))
	m = re.search(r'Json = @"(.*)";', text, re.S)
	return json.loads(m.group(1).replace('""', '"'))["addons"]


def main():
	if len(sys.argv) < 2:
		raise SystemExit(__doc__)
	src = sys.argv[1]
	version = sys.argv[2] if len(sys.argv) > 2 else re.search(r'^## Version:\s*(\S+)', read(os.path.join(src, "EldoUI.toc")), re.M).group(1)
	mods = modules(src)
	da = danish(src)
	replace = [(addon, name) for name, info in mods.items() for addon in info["conflicts"]] + competitors(src)

	covered = {}
	for entry in catalog():
		folders = {f.lower() for f in entry.get("folders", [])}
		names = []
		for addon, module in replace:
			if addon.lower() in folders and module in mods and module not in names:
				names.append(module)
		if names:
			names.sort(key=lambda n: mods[n]["title"])
			covered[entry["id"]] = [{"module": n, "en": mods[n]["title"], "da": da.get(mods[n]["title"], mods[n]["title"])} for n in names]

	data = {"eldoui": version, "addons": dict(sorted(covered.items()))}
	body = json.dumps(data, indent="\t", ensure_ascii=False).replace('"', '""')
	out = os.path.join(ROOT, "src", "Core", "CoverageData.cs")
	with open(out, "w", encoding="utf-8", newline="\n") as f:
		f.write("// Which catalog addons an EldoUI module replaces one to one, with the module names in English and Danish.\n")
		f.write("// Generated by tools/build_coverage.py from EldoUI's own list of addons that do a module's job. Do not edit by hand.\n")
		f.write("namespace EldoUI.Updater.Core\n{\n\tstatic class CoverageData\n\t{\n")
		f.write('\t\tpublic const string Json = @"' + body + '";\n')
		f.write("\t}\n}\n")
	print("%d catalog addons covered by EldoUI %s" % (len(covered), version))
	for key, value in data["addons"].items():
		print("  %-24s %s" % (key, ", ".join(v["en"] for v in value)))


if __name__ == "__main__":
	main()
