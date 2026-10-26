#!/usr/bin/env python3
"""Builds src/Core/CoverageData.cs: what EldoUI does for each catalog addon.

Usage: python3 tools/build_coverage.py <EldoUI source folder> [EldoUI version]

Three kinds of note, each naming EldoUI modules in English and Danish (from EldoUI's own Danish texts):
  full   an EldoUI module does the addon's job
  part   an EldoUI module does part of it
  works  EldoUI works with the addon (places or styles it)

Two sources:
  1. EldoUI's own list of other addons that do a module's job: a module's `conflicts` list and the
     rows in Data/Competitors.lua. A row's `covers` ("full" or "part") says how much of the addon's
     job EldoUI does; a row without it counts as "full" for "replace" and "part" otherwise.
  2. tools/coverage-review.json: the reviewed answer per catalog addon. A scope it names ("full",
     "part" or "works") replaces what EldoUI's list says for that scope; a scope it leaves out keeps
     EldoUI's list. Its "features" are EldoUI features that are not modules (key: English name).

Run it again for every EldoUI release, review new catalog addons into coverage-review.json, and review
the diff with the release. --diff lists where the review and EldoUI's own list disagree.
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


COVERS = ("full", "part")
# EldoUI core parts that are no module, by their registry feature key.
CORE_PARTS = {"windowmover": "WindowMover", "uiscale": "UIScale"}


def covers_of(line, default):
	m = re.search(r'covers = "(full|part)"', line)
	return m.group(1) if m else default


def registry(src, mods):
	"""Every entry of EldoUI's own list as (addon folder, module, covers).

	`covers` says how much of the addon's job EldoUI does ("full" or "part"); an entry without it
	counts as "full" for a whole module ("replace") and "part" otherwise. Entries with scope
	"alongside" stand nothing back but count the same way."""
	text = read(os.path.join(src, "Data", "Competitors.lua"))
	found = {}

	def put(addon, module, covers):
		found[(addon, module)] = covers

	for name, info in mods.items():
		for addon in info["conflicts"]:
			put(addon, name, "full")
	# for _, row in ipairs(NAME) do Add({ addon = row[1], module = row[2], scope = "...", covers = row[3] ...
	for m in re.finditer(r'for _, row in ipairs\((\w+)\) do\s*\n\s*Add\(\{ addon = row\[1\], module = row\[2\], scope = "(\w+)"([^\n]*)', text):
		name, scope, rest = m.groups()
		for r in table_rows(text, name):
			covers = r[2] if "covers = row[3]" in rest and len(r) > 2 and r[2] in COVERS else ("full" if scope == "replace" else "part")
			put(r[0], r[1], covers)
	# for _, row in ipairs(NAME) do Add({ addon = "X", module = row[1], ... scope = "overlap"  (Leatrix)
	for name, addon, rest in re.findall(r'for _, row in ipairs\((\w+)\) do\s*\n\s*Add\(\{ addon = "([^"]+)", module = row\[1\],([^\n]*)', text):
		for r in table_rows(text, name):
			put(addon, r[0], covers_of(rest, "part"))
	# for module, read in pairs(NAME) do Add({ addon = "X", module = module, scope = "replace" ...  (ElvUI)
	for name, addon in re.findall(r'for module, \w+ in pairs\((\w+)\) do\s*\n\s*Add\(\{ addon = "([^"]+)", module = module, scope = "replace"', text):
		m = re.search(r'local ' + name + r' = \{\n(.*?)\n\}', text, re.S)
		for key in re.findall(r'^\s*(\w+) = function', m.group(1), re.M):
			put(addon, key, "full")
	# for _, addon in ipairs({ "A", "B" }) do Add({ addon = addon, module = "Y", ... covers = TABLE[addon] or "x"
	for names, module, rest in re.findall(r'for _, addon in ipairs\(\{([^}]*)\}\) do\s*\n\s*Add\(\{ addon = addon, module = "([^"]+)",([^\n]*)', text):
		table = re.search(r'covers = (\w+)\[addon\]', rest)
		per = {}
		if table:
			m = re.search(r'local ' + table.group(1) + r' = \{([^}]*)\}', text)
			per = dict(re.findall(r'(\w+) = "(full|part)"', m.group(1))) if m else {}
		for addon in strings(names):
			put(addon, module, per.get(addon, covers_of(rest, "part")))
	# Single Add({ addon = "X", module = "Y", ... }) and Add({ addon = "X", feature = "core part", ... }) lines.
	for addon, module, rest in re.findall(r'^Add\(\{ addon = "([^"]+)", module = "([^"]+)",([^\n]*)', text, re.M):
		scope = re.search(r'scope = "(\w+)"', rest)
		put(addon, module, covers_of(rest, "full" if not scope or scope.group(1) == "replace" else "part"))
	for addon, feature, rest in re.findall(r'^Add\(\{ addon = "([^"]+)", feature = "([^"]+)",([^\n]*)', text, re.M):
		if feature in CORE_PARTS:
			put(addon, CORE_PARTS[feature], covers_of(rest, "part"))
	if not found:
		raise SystemExit("Data/Competitors.lua: no entries found")
	return found


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


SCOPES = ("full", "part", "works")


def main():
	args = [a for a in sys.argv[1:] if not a.startswith("--")]
	if not args:
		raise SystemExit(__doc__)
	src = args[0]
	version = args[1] if len(args) > 1 else re.search(r'^## Version:\s*(\S+)', read(os.path.join(src, "EldoUI.toc")), re.M).group(1)
	mods = modules(src)
	da = danish(src)
	review = json.loads(read(os.path.join(ROOT, "tools", "coverage-review.json")))
	features = review.get("features", {})
	names = {key: info["title"] for key, info in mods.items()}
	names.update(features)
	entries = registry(src, mods)
	registry_lists = {scope: [(addon, module) for (addon, module), covers in entries.items() if covers == scope] for scope in COVERS}

	covered, unreviewed, disagree = {}, [], []
	for entry in catalog():
		folders = {f.lower() for f in entry.get("folders", [])}
		reviewed = review["addons"].get(entry["id"])
		if reviewed is None:
			unreviewed.append(entry["id"])
		notes = []
		for scope in SCOPES:
			listed = []
			for addon, module in registry_lists.get(scope, []):
				if addon.lower() in folders and (module in mods or module in features) and module not in listed:
					listed.append(module)
			chosen = reviewed[scope] if reviewed is not None and scope in reviewed else listed
			if reviewed is not None and scope in reviewed and sorted(listed) != sorted(chosen) and scope in registry_lists:
				disagree.append("%-24s %-5s EldoUI's list: %s; review: %s" % (entry["id"], scope, ", ".join(listed) or "-", ", ".join(chosen) or "-"))
			for module in chosen:
				if module not in names:
					raise SystemExit("coverage-review.json: %s names %s, which is no EldoUI module or feature" % (entry["id"], module))
				if any(n["module"] == module for n in notes):
					continue
				notes.append({"module": module, "scope": scope, "en": names[module], "da": da.get(names[module], names[module])})
		if notes:
			covered[entry["id"]] = notes

	data = {"eldoui": version, "addons": dict(sorted(covered.items()))}
	body = json.dumps(data, indent="\t", ensure_ascii=False).replace('"', '""')
	out = os.path.join(ROOT, "src", "Core", "CoverageData.cs")
	with open(out, "w", encoding="utf-8", newline="\n") as f:
		f.write("// What EldoUI does for each catalog addon (full, part, works with), with the module names in English and Danish.\n")
		f.write("// Generated by tools/build_coverage.py from EldoUI's own list and tools/coverage-review.json. Do not edit by hand.\n")
		f.write("namespace EldoUI.Updater.Core\n{\n\tstatic class CoverageData\n\t{\n")
		f.write('\t\tpublic const string Json = @"' + body + '";\n')
		f.write("\t}\n}\n")
	count = {scope: sum(1 for v in covered.values() if v[0]["scope"] == scope) for scope in SCOPES}
	print("EldoUI %s: %d catalog addons with a note (%d full, %d part, %d works with)" % (version, len(covered), count["full"], count["part"], count["works"]))
	if unreviewed:
		print("Not reviewed yet (EldoUI's own list only): " + ", ".join(unreviewed))
	if "--diff" in sys.argv:
		print("Where the review and EldoUI's own list disagree:")
		for line in disagree:
			print("  " + line)


if __name__ == "__main__":
	main()
