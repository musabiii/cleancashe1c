using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace CleanCache1C
{
    /// <summary>Профиль пользователя Windows, в котором ищем список баз и кэш.</summary>
    class Profile
    {
        public string Title;
        public string RoamingAppData;
        public string LocalAppData;
        public bool IsCurrent;

        public override string ToString() { return Title; }
    }

    /// <summary>Информационная база из ibases.v8i.</summary>
    class InfoBase
    {
        public string Name;
        public string Folder;
        public string Connect;
        public string Id;
        public bool InList;
    }

    /// <summary>Строка списка: база и найденные для неё папки кэша и настроек.</summary>
    class Row
    {
        public InfoBase Base;
        public readonly List<string> LocalDirs = new List<string>();
        public readonly List<string> RoamingDirs = new List<string>();
        public long LocalSize = -1;
        public long RoamingSize = -1;
        public ListViewItem Item;
    }

    static class Scanner
    {
        // 1cv8 — платформа 8.3, 1cv82 — старая 8.2
        static readonly string[] CacheRoots = { "1cv8", "1cv82" };

        public static List<Profile> FindProfiles()
        {
            var current = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var list = new List<Profile>
            {
                // для текущего пользователя берём реальные пути: AppData на RDP бывает перенаправлена
                new Profile
                {
                    Title = Path.GetFileName(current) + " (текущий)",
                    RoamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    IsCurrent = true
                }
            };

            try
            {
                foreach (var dir in Directory.GetDirectories(Path.GetDirectoryName(current)).OrderBy(d => d))
                {
                    if (string.Equals(dir, current, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var profile = ProfileFromDir(dir);
                    profile.Title = Path.GetFileName(dir);
                    // без прав администратора чужие профили не читаются — Exists вернёт false
                    if (Directory.Exists(Path.Combine(profile.RoamingAppData, "1C")) || Directory.Exists(Path.Combine(profile.LocalAppData, "1C")))
                        list.Add(profile);
                }
            }
            catch (Exception) { }

            return list;
        }

        public static Profile ProfileFromDir(string dir)
        {
            dir = Path.GetFullPath(dir);
            return new Profile
            {
                Title = Path.GetFileName(dir.TrimEnd('\\')),
                RoamingAppData = Path.Combine(dir, @"AppData\Roaming"),
                LocalAppData = Path.Combine(dir, @"AppData\Local")
            };
        }

        public static List<Row> BuildRows(Profile profile, List<string> warnings)
        {
            var rows = new List<Row>();
            var byId = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);

            foreach (var ib in ReadInfoBases(profile, warnings))
            {
                if (byId.ContainsKey(ib.Id))
                    continue;
                var row = new Row { Base = ib };
                byId[ib.Id] = row;
                rows.Add(row);
            }

            foreach (var root in CacheRoots)
            {
                CollectDirs(Path.Combine(profile.LocalAppData, "1C", root), byId, rows, r => r.LocalDirs);
                CollectDirs(Path.Combine(profile.RoamingAppData, "1C", root), byId, rows, r => r.RoamingDirs);
            }

            return rows;
        }

        static void CollectDirs(string root, Dictionary<string, Row> byId, List<Row> rows, Func<Row, List<string>> target)
        {
            if (!Directory.Exists(root))
                return;

            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                Guid guid;
                if (!Guid.TryParse(name, out guid))
                    continue;

                Row row;
                if (!byId.TryGetValue(name, out row))
                {
                    row = new Row { Base = new InfoBase { Name = "‹нет в списке баз›", Folder = "", Connect = "", Id = name, InList = false } };
                    byId[name] = row;
                    rows.Add(row);
                }
                target(row).Add(dir);
            }
        }

        static IEnumerable<InfoBase> ReadInfoBases(Profile profile, List<string> warnings)
        {
            var estart = Path.Combine(profile.RoamingAppData, @"1C\1CEStart");
            var files = new List<string> { Path.Combine(estart, "ibases.v8i") };

            // общие списки баз, подключённые в настройках 1CEStart (пользовательских и общих для компьютера)
            var cfgs = new[]
            {
                Path.Combine(estart, "1cestart.cfg"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"1C\1CEStart\1cestart.cfg")
            };
            foreach (var cfg in cfgs.Where(File.Exists))
            {
                try
                {
                    foreach (var line in File.ReadAllLines(cfg, Encoding.UTF8))
                    {
                        var eq = line.IndexOf('=');
                        if (eq <= 0 || !line.Substring(0, eq).Trim().Equals("CommonInfoBases", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var path = Environment.ExpandEnvironmentVariables(line.Substring(eq + 1).Trim());
                        if (path.Length > 0 && !path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            files.Add(path);
                    }
                }
                catch (Exception ex) { warnings.Add(cfg + ": " + ex.Message); }
            }

            var result = new List<InfoBase>();
            foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (File.Exists(file))
                        result.AddRange(ParseV8i(file));
                    else if (file != files[0])
                        warnings.Add("Не найден общий список баз: " + file);
                }
                catch (Exception ex) { warnings.Add(file + ": " + ex.Message); }
            }
            return result;
        }

        static IEnumerable<InfoBase> ParseV8i(string path)
        {
            var result = new List<InfoBase>();
            InfoBase cur = null;

            // ReadAllLines сам распознаёт BOM (UTF-8 / UTF-16)
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    cur = new InfoBase { Name = line.Substring(1, line.Length - 2), Folder = "", InList = true };
                    result.Add(cur);
                    continue;
                }
                if (cur == null)
                    continue;

                var eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();

                if (key.Equals("Connect", StringComparison.OrdinalIgnoreCase))
                    cur.Connect = value;
                else if (key.Equals("ID", StringComparison.OrdinalIgnoreCase))
                    cur.Id = value;
                else if (key.Equals("Folder", StringComparison.OrdinalIgnoreCase))
                    cur.Folder = value.TrimStart('/');
            }

            // секции без Connect — это группы (папки) в списке баз, а не базы
            return result.Where(b => !string.IsNullOrEmpty(b.Connect) && !string.IsNullOrEmpty(b.Id));
        }

        static readonly Regex PwdRegex = new Regex("(Pwd\\s*=\\s*)(\"[^\"]*\"|[^;]*)", RegexOptions.IgnoreCase);

        /// <summary>Строка подключения без пароля — для показа в списке.</summary>
        public static string MaskPassword(string connect)
        {
            return PwdRegex.Replace(connect ?? "", "$1***");
        }

        public static long DirSize(string path, CancellationToken ct)
        {
            long total = 0;
            var stack = new Stack<string>();
            stack.Push(path);

            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var dir = new DirectoryInfo(stack.Pop());
                try
                {
                    foreach (var fsi in dir.EnumerateFileSystemInfos())
                    {
                        var file = fsi as FileInfo;
                        if (file != null)
                            total += file.Length;
                        else if ((fsi.Attributes & FileAttributes.ReparsePoint) == 0)
                            stack.Push(fsi.FullName);
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
            return total;
        }

        /// <summary>Удаляет папку целиком. Возвращает число файлов, которые удалить не удалось.</summary>
        public static int DeleteDir(string path)
        {
            if (!Directory.Exists(path))
                return 0;
            int failed = 0;
            DeleteTree(new DirectoryInfo(path), ref failed);
            return failed;
        }

        static void DeleteTree(DirectoryInfo dir, ref int failed)
        {
            FileSystemInfo[] items;
            try { items = dir.GetFileSystemInfos(); }
            catch (Exception) { failed++; return; }

            foreach (var fsi in items)
            {
                var sub = fsi as DirectoryInfo;
                if (sub != null && (sub.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    DeleteTree(sub, ref failed);
                    continue;
                }
                try
                {
                    // ссылки (junction) удаляются сами, без захода внутрь
                    fsi.Attributes = FileAttributes.Normal;
                    fsi.Delete();
                }
                catch (Exception) { failed++; }
            }

            try
            {
                dir.Attributes = FileAttributes.Normal;
                dir.Delete();
            }
            catch (Exception) { /* папка не пуста из-за занятых файлов — они уже посчитаны */ }
        }

        public static string FormatSize(long bytes)
        {
            string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return unit == 0 ? bytes + " Б" : value.ToString("0.0") + " " + units[unit];
        }
    }
}
