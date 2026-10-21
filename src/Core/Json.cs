// A small JSON reader and writer, enough for GitHub's release API and the settings file.
// Objects become Dictionary<string, object>, arrays List<object>, numbers double, and null stays null.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EldoUI.Updater.Core
{
	public static class Json
	{
		public static object Parse(string text)
		{
			if (text == null) throw new ArgumentNullException(nameof(text));
			var reader = new Reader(text);
			reader.SkipSpace();
			object value = reader.ReadValue(0);
			reader.SkipSpace();
			if (!reader.AtEnd) throw new FormatException("Unexpected text after the JSON value.");
			return value;
		}

		public static Dictionary<string, object> ParseObject(string text)
		{
			var obj = Parse(text) as Dictionary<string, object>;
			if (obj == null) throw new FormatException("Expected a JSON object.");
			return obj;
		}

		public static string Str(this Dictionary<string, object> obj, string key)
		{
			object v;
			return obj != null && obj.TryGetValue(key, out v) ? v as string : null;
		}

		public static bool Bool(this Dictionary<string, object> obj, string key, bool fallback = false)
		{
			object v;
			return obj != null && obj.TryGetValue(key, out v) && v is bool ? (bool)v : fallback;
		}

		public static double Num(this Dictionary<string, object> obj, string key, double fallback = 0)
		{
			object v;
			return obj != null && obj.TryGetValue(key, out v) && v is double ? (double)v : fallback;
		}

		public static List<object> Arr(this Dictionary<string, object> obj, string key)
		{
			object v;
			return obj != null && obj.TryGetValue(key, out v) ? v as List<object> : null;
		}

		public static string Write(object value)
		{
			var sb = new StringBuilder();
			WriteValue(sb, value, 0);
			return sb.ToString();
		}

		static void WriteValue(StringBuilder sb, object value, int indent)
		{
			if (value == null) { sb.Append("null"); return; }
			var s = value as string;
			if (s != null) { WriteString(sb, s); return; }
			if (value is bool) { sb.Append((bool)value ? "true" : "false"); return; }
			if (value is int || value is long || value is double)
			{
				sb.Append(Convert.ToDouble(value).ToString("R", CultureInfo.InvariantCulture));
				return;
			}
			var dict = value as IDictionary<string, object>;
			if (dict != null)
			{
				sb.Append('{');
				bool first = true;
				foreach (var kv in dict)
				{
					sb.Append(first ? "\n" : ",\n");
					first = false;
					sb.Append(' ', (indent + 1) * 2);
					WriteString(sb, kv.Key);
					sb.Append(": ");
					WriteValue(sb, kv.Value, indent + 1);
				}
				if (!first) { sb.Append('\n').Append(' ', indent * 2); }
				sb.Append('}');
				return;
			}
			var list = value as System.Collections.IEnumerable;
			if (list != null)
			{
				sb.Append('[');
				bool first = true;
				foreach (var item in list)
				{
					if (!first) sb.Append(", ");
					first = false;
					WriteValue(sb, item, indent + 1);
				}
				sb.Append(']');
				return;
			}
			WriteString(sb, value.ToString());
		}

		static void WriteString(StringBuilder sb, string s)
		{
			sb.Append('"');
			foreach (char c in s)
			{
				switch (c)
				{
					case '"': sb.Append("\\\""); break;
					case '\\': sb.Append("\\\\"); break;
					case '\n': sb.Append("\\n"); break;
					case '\r': sb.Append("\\r"); break;
					case '\t': sb.Append("\\t"); break;
					default:
						if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
						else sb.Append(c);
						break;
				}
			}
			sb.Append('"');
		}

		sealed class Reader
		{
			readonly string _s;
			int _i;
			const int MaxDepth = 64;

			public Reader(string s) { _s = s; }
			public bool AtEnd { get { return _i >= _s.Length; } }

			public void SkipSpace()
			{
				while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r' || _s[_i] == '﻿')) _i++;
			}

			public object ReadValue(int depth)
			{
				if (depth > MaxDepth) throw new FormatException("JSON is nested too deeply.");
				if (AtEnd) throw new FormatException("Unexpected end of JSON.");
				char c = _s[_i];
				switch (c)
				{
					case '{': return ReadObject(depth);
					case '[': return ReadArray(depth);
					case '"': return ReadString();
					case 't': Expect("true"); return true;
					case 'f': Expect("false"); return false;
					case 'n': Expect("null"); return null;
					default:
						if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
						throw new FormatException("Unexpected character '" + c + "' in JSON.");
				}
			}

			void Expect(string word)
			{
				if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw new FormatException("Expected " + word + ".");
				_i += word.Length;
			}

			Dictionary<string, object> ReadObject(int depth)
			{
				var obj = new Dictionary<string, object>(StringComparer.Ordinal);
				_i++;
				SkipSpace();
				if (!AtEnd && _s[_i] == '}') { _i++; return obj; }
				while (true)
				{
					SkipSpace();
					if (AtEnd || _s[_i] != '"') throw new FormatException("Expected a property name.");
					string key = ReadString();
					SkipSpace();
					if (AtEnd || _s[_i] != ':') throw new FormatException("Expected ':'.");
					_i++;
					SkipSpace();
					obj[key] = ReadValue(depth + 1);
					SkipSpace();
					if (AtEnd) throw new FormatException("Unexpected end of JSON object.");
					if (_s[_i] == ',') { _i++; continue; }
					if (_s[_i] == '}') { _i++; return obj; }
					throw new FormatException("Expected ',' or '}'.");
				}
			}

			List<object> ReadArray(int depth)
			{
				var list = new List<object>();
				_i++;
				SkipSpace();
				if (!AtEnd && _s[_i] == ']') { _i++; return list; }
				while (true)
				{
					SkipSpace();
					list.Add(ReadValue(depth + 1));
					SkipSpace();
					if (AtEnd) throw new FormatException("Unexpected end of JSON array.");
					if (_s[_i] == ',') { _i++; continue; }
					if (_s[_i] == ']') { _i++; return list; }
					throw new FormatException("Expected ',' or ']'.");
				}
			}

			string ReadString()
			{
				var sb = new StringBuilder();
				_i++;
				while (true)
				{
					if (AtEnd) throw new FormatException("Unterminated string.");
					char c = _s[_i++];
					if (c == '"') return sb.ToString();
					if (c != '\\') { sb.Append(c); continue; }
					if (AtEnd) throw new FormatException("Unterminated escape.");
					char e = _s[_i++];
					switch (e)
					{
						case '"': sb.Append('"'); break;
						case '\\': sb.Append('\\'); break;
						case '/': sb.Append('/'); break;
						case 'b': sb.Append('\b'); break;
						case 'f': sb.Append('\f'); break;
						case 'n': sb.Append('\n'); break;
						case 'r': sb.Append('\r'); break;
						case 't': sb.Append('\t'); break;
						case 'u':
							if (_i + 4 > _s.Length) throw new FormatException("Bad \\u escape.");
							sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
							_i += 4;
							break;
						default: throw new FormatException("Bad escape \\" + e + ".");
					}
				}
			}

			double ReadNumber()
			{
				int start = _i;
				if (_s[_i] == '-') _i++;
				while (_i < _s.Length && "0123456789.eE+-".IndexOf(_s[_i]) >= 0) _i++;
				double d;
				if (!double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
					throw new FormatException("Bad number in JSON.");
				return d;
			}
		}
	}
}
