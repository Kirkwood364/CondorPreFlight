using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CondorPreFlight
{
    // Line-preserving INI editor: only the lines we change are touched,
    // so comments, ordering and unknown keys in Condor / instrument files survive.
    public class IniFile
    {
        public string Path;
        public bool Dirty;
        private List<string> lines = new List<string>();
        private Encoding enc = Encoding.Default;
        private bool bom;
        private string newline = "\r\n";

        public static IniFile Load(string path)
        {
            IniFile f = new IniFile();
            f.Path = path;
            byte[] b = File.ReadAllBytes(path);
            int skip = 0;
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { f.enc = new UTF8Encoding(false); f.bom = true; skip = 3; }
            else if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { f.enc = new UnicodeEncoding(false, false); f.bom = true; skip = 2; }
            else if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { f.enc = new UnicodeEncoding(true, false); f.bom = true; skip = 2; }
            string text = f.enc.GetString(b, skip, b.Length - skip);
            if (text.IndexOf("\r\n") < 0 && text.IndexOf('\n') >= 0) f.newline = "\n";
            f.lines = new List<string>(text.Replace("\r\n", "\n").Split('\n'));
            return f;
        }

        public static IniFile FromText(string path, string text)
        {
            IniFile f = new IniFile();
            f.Path = path;
            f.lines = new List<string>(text.Replace("\r\n", "\n").Split('\n'));
            f.Dirty = true;
            return f;
        }

        public void Save()
        {
            string text = string.Join(newline, lines.ToArray());
            byte[] body = enc.GetBytes(text);
            using (FileStream fs = new FileStream(Path, FileMode.Create, FileAccess.Write))
            {
                if (bom) { byte[] p = enc.GetBytes("\uFEFF"); fs.Write(p, 0, p.Length); }
                fs.Write(body, 0, body.Length);
            }
            Dirty = false;
        }

        private static string SectionName(string line)
        {
            string t = line.Trim();
            if (t.Length >= 2 && t[0] == '[')
            {
                int end = t.IndexOf(']');
                if (end > 0) return t.Substring(1, end - 1).Trim();
            }
            return null;
        }

        // Returns [start, end) line range of the section body, or false if absent.
        private bool FindSection(string section, out int start, out int end)
        {
            start = -1; end = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                string s = SectionName(lines[i]);
                if (s == null) continue;
                if (start >= 0) { end = i; return true; }
                if (string.Equals(s, section, StringComparison.OrdinalIgnoreCase)) start = i + 1;
            }
            return start >= 0;
        }

        private int FindKey(int start, int end, string key)
        {
            for (int i = start; i < end; i++)
            {
                string t = lines[i].TrimStart();
                if (t.Length == 0 || t[0] == ';' || t[0] == '#') continue;
                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                if (string.Equals(t.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        public string Get(string section, string key, string fallback)
        {
            int s, e;
            if (!FindSection(section, out s, out e)) return fallback;
            int i = FindKey(s, e, key);
            if (i < 0) return fallback;
            string line = lines[i];
            return line.Substring(line.IndexOf('=') + 1).Trim();
        }

        public bool Set(string section, string key, string value)
        {
            int s, e;
            if (!FindSection(section, out s, out e))
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                lines.Add("[" + section + "]");
                lines.Add(key + "=" + value);
                Dirty = true;
                return true;
            }
            int i = FindKey(s, e, key);
            if (i >= 0)
            {
                string line = lines[i];
                int eq = line.IndexOf('=');
                if (line.Substring(eq + 1).Trim() == value) return false;
                lines[i] = line.Substring(0, eq + 1) + value;
                Dirty = true;
                return true;
            }
            int at = e;
            while (at > s && lines[at - 1].Trim().Length == 0) at--;
            lines.Insert(at, key + "=" + value);
            Dirty = true;
            return true;
        }
    }
}
