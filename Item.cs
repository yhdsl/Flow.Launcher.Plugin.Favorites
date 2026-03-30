
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using NPinyin;

namespace Flow.Launcher.Plugin.Favorites
{
    class Item
    {
        public string Name { get; set; }
        public string Value { get; set; }

        public string PinyinFull { get; set; }
        public string PinyinInitials { get; set; }

        string _iconPath;
        
        public string IconPath {
            get {
                if (_iconPath == null)
                {
                    string value = Value;

                    if (value != null && value.Contains("%"))
                        value = ExpandEnvVars(value);

                    if (value != null && value.StartsWith("http"))
                        _iconPath = Path.Combine(Favorites.AssemblyDirectory, "Icons\\Web.ico");
                    else if (value != null && value.StartsWith("shell:"))
                        _iconPath = @"C:\Windows\explorer.exe";
                    else if (value != null && value.Contains(".") && File.Exists(value))
                    {
                        string txtIconPath = Path.Combine(Favorites.AssemblyDirectory, "Icons\\txt.ico");

                        if (value.EndsWith(".txt") && File.Exists(txtIconPath))
                            _iconPath = txtIconPath;
                        else
                            _iconPath = value;
                    }
                    else if (Directory.Exists(value))
                        _iconPath = @"C:\Windows\explorer.exe";
                    else
                        _iconPath = Path.Combine(Favorites.AssemblyDirectory, "Icons\\CommandLine.ico");
                }

                return _iconPath;
            }
        }

        public static void InitPinyin(Item item)
        {
            if (item == null || string.IsNullOrEmpty(item.Name))
                return;

            try
            {
                item.PinyinFull = Pinyin.GetPinyin(item.Name)
                    .Replace(" ", "")
                    .ToLower();

                item.PinyinInitials = GetInitials(item.Name);
            }
            catch
            {
                item.PinyinFull = "";
                item.PinyinInitials = "";
            }
        }

        static string GetInitials(string text)
        {
            string result = "";

            foreach (char c in text)
            {
                try
                {
                    string py = Pinyin.GetPinyin(c.ToString());
                    if (!string.IsNullOrEmpty(py))
                        result += py[0];
                    else
                        result += c;
                }
                catch
                {
                    result += c;
                }
            }

            return result.ToLower();
        }

        public void Execute(bool asAdmin = false)
        {
            string value = Value;

            if (string.IsNullOrEmpty(value))
                return;

            if (value.Contains("%"))
                value = ExpandEnvVars(value);

            bool isFolder = Directory.Exists(value);

            if (value.Length > 3 && value[1..].StartsWith(":\\") &&
                value.Contains(" ") && (File.Exists(value) || Directory.Exists(value)))

                value = "\"" + value + "\"";

            Match match = Regex.Match(value, "((?<file>[^\\s\"]+)|\"(?<file>.+?)\") *(?<args>[^\\f\\r]*)");

            var info = new ProcessStartInfo();

            bool showParent = (Keyboard.IsKeyDown(Key.LeftCtrl)  || Keyboard.IsKeyDown(Key.RightCtrl)) &&
                             !(Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift));

            if (showParent)
                info.FileName = Path.GetDirectoryName(match.Groups["file"].Value);
            else
            {
                if (isFolder)
                {
                    info.FileName = Favorites.FolderAppPath;
                    info.Arguments = Favorites.FolderAppArguments.Replace("%1",  match.Groups["file"].Value);
                }
                else
                {
                    info.FileName = match.Groups["file"].Value;
                    info.Arguments = match.Groups["args"].Value;
                }
            }
            
            info.UseShellExecute = true;

            if (asAdmin)
                info.Verb = "runas";

            using Process p = new Process() { StartInfo = info };

            try {
                p.Start();
            } catch { }
        }

        static string ExpandEnvVars(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            if (value.Contains("%"))
                value = Environment.ExpandEnvironmentVariables(value);

            return value;
        }

        public static List<Item> LoadFile(string path)
        {
            List<Item> ret = new List<Item>();

            if (!File.Exists(path))
                return ret;

            foreach (string it in File.ReadAllLines(path))
            {
                string line = it.Trim();

                if (line.StartsWith("#folder-app-path:"))
                    Favorites.FolderAppPath = line.Substring(line.IndexOf(":") + 1).Trim();
                else if (line.StartsWith("#folder-app-args:"))
                    Favorites.FolderAppArguments = line.Substring(line.IndexOf(":") + 1).Trim();
                
                if (line.StartsWith("#") || !line.Contains("="))
                    continue;

                Item item = new Item() {
                    Name = line.Substring(0, line.IndexOf("=")).Trim(),
                    Value = line.Substring(line.IndexOf("=") + 1).Trim()
                };

                InitPinyin(item);

                ret.Add(item);
            }
            
            return ret;
        }

        static Dictionary<string, string[]> FuzzyMap = new()
        {
            { "zh", new[] { "z" } },
            { "ch", new[] { "c" } },
            { "sh", new[] { "s" } },
            { "ang", new[] { "an" } },
            { "eng", new[] { "en" } },
            { "ing", new[] { "in" } },
            { "n", new[] { "l" } },
            { "h", new[] { "f" } },
            { "l", new[] { "r" } },
            { "g", new[] { "k" } },
            { "iang", new[] { "ian" } },
            { "uang", new[] { "uan" } },
        };

        static bool FuzzyMatch(string text, string input)
        {
            if (text.Contains(input))
                return true;

            foreach (var kv in FuzzyMap)
            {
                foreach (var alt in kv.Value)
                {
                    if (text.Replace(kv.Key, alt).Contains(input))
                        return true;
                }
            }

            return false;
        }

        public static List<Item> Filter(List<Item> items, string value)
        {
            List<Item> ret = new List<Item>();

            if (string.IsNullOrEmpty(value))
                return ret;

            string[] searches = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string valueLower = value.ToLower();

            if (value.Length == 1)
            {
                foreach (Item item in items)
                {
                    if (item.Name.Contains("__"))
                        item.Name.Replace("__", "!dbl!");

                    int index = item.Name.IndexOf("_");

                    if (index > -1 && item.Name.Length > index + 1 && item.Name.ToLower()[index + 1] == value[0])
                        ret.Add(item);

                    if (item.Name.Contains("!dbl!"))
                        item.Name = item.Name.Replace("!dbl!", "_");
                }

                return ret;
            }

            // all searches
            if (searches.Length > 1)
            {
                foreach (Item item in items)
                {
                    bool notFound = false;

                    foreach (string search in searches)
                    {
                        string searchLower = search.ToLower();

                        string nameLower = item.Name.ToLower();
                        string valueLowerItem = item.Value.ToLower();

                        string namePinyin = NPinyin.Pinyin.GetPinyin(item.Name).Replace(" ", "").ToLower();
                        string valuePinyin = NPinyin.Pinyin.GetPinyin(item.Value).Replace(" ", "").ToLower();

                        if (!nameLower.Contains(searchLower) &&
                            !valueLowerItem.Contains(searchLower) &&
                            !namePinyin.Contains(searchLower) &&
                            !valuePinyin.Contains(searchLower))
                        {
                            notFound = true;
                            break;
                        }
                    }

                    if (notFound)
                        continue;
                    else
                        if (!ret.Contains(item))
                            ret.Add(item);
                }
            }

            // upper chars
            foreach (Item item in items)
            {
                if (ret.Contains(item))
                    continue;

                string upperChars = "";

                foreach (char ch in item.Name)
                    if (char.IsUpper(ch))
                        upperChars += ch;

                if (upperChars.ToLower().Contains(valueLower) && !ret.Contains(item))
                    ret.Add(item);
            }

            // name starts with
            foreach (Item item in items)
                if (item.Name.ToLower().StartsWith(valueLower) ||
                    NPinyin.Pinyin.GetPinyin(item.Name).Replace(" ", "").ToLower().StartsWith(valueLower))
                    if (!ret.Contains(item))
                        ret.Add(item);

            // name contains
            foreach (Item item in items)
                if (item.Name.ToLower().Contains(valueLower) ||
                    NPinyin.Pinyin.GetPinyin(item.Name).Replace(" ", "").ToLower().Contains(valueLower))
                    if (!ret.Contains(item))
                        ret.Add(item);

            // value contains
            foreach (Item item in items)
                if (item.Value.ToLower().Contains(valueLower) ||
                    NPinyin.Pinyin.GetPinyin(item.Value).Replace(" ", "").ToLower().Contains(valueLower))
                    if (!ret.Contains(item))
                        ret.Add(item);

            return ret;
        }
    }
}
