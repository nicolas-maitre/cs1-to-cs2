// Choosing a package: a pasted path (folder, its cs1conv-plan.json, or the web tool's .zip), Windows' own file dialog
// (the settings UI can't browse), packages found under %LOCALAPPDATA%\cs1-to-cs2, and the last five used.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;

namespace Cs2ImporterMod
{
    public static class Packages
    {
        public const string PlanName = "cs1conv-plan.json";
        const int MaxRecent = 5;

        public static string LocalRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cs1-to-cs2"); }
        }

        /// <summary>Where zips are unpacked (and where the dropdown looks first).</summary>
        public static string PackagesRoot { get { return Path.Combine(LocalRoot, "packages"); } }

        // ---------------------------------------------------------------- resolving

        /// <summary>
        /// The plan file for what the user gave: a folder (or one level below it, as a zip unpacked with its own folder),
        /// a cs1conv-plan.json, or a .zip (unpacked under packages\ first). Quotes and spaces around a pasted path are
        /// ignored. Throws InvalidOperationException with a message for the status line.
        /// </summary>
        public static string Resolve(string input, out string unpackedFrom)
        {
            unpackedFrom = null;
            var p = (input ?? "").Trim().Trim('"', '\'').Trim();
            if (p.Length == 0) throw new InvalidOperationException(T.S("err.noPackage"));
            p = Environment.ExpandEnvironmentVariables(p);
            if (File.Exists(p) && p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                unpackedFrom = p;
                p = Unzip(p);
            }
            if (File.Exists(p))
            {
                if (!string.Equals(Path.GetFileName(p), PlanName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(T.S("err.notPackage", Path.GetFileName(p)));
                return Path.GetFullPath(p);
            }
            if (Directory.Exists(p))
            {
                var plan = FindPlan(p, 2);
                if (plan != null) return plan;
            }
            throw new InvalidOperationException(T.S("err.noPlan", p));
        }

        static string FindPlan(string dir, int depth)
        {
            var f = Path.Combine(dir, PlanName);
            if (File.Exists(f)) return Path.GetFullPath(f);
            if (depth <= 0) return null;
            foreach (var d in Directory.GetDirectories(dir))
            {
                var r = FindPlan(d, depth - 1);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>Unpacks a package zip to packages\&lt;zip name&gt;\ (replacing an earlier copy) and returns that folder.</summary>
        static string Unzip(string zip)
        {
            var name = Path.GetFileNameWithoutExtension(zip);
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            var dest = Path.Combine(PackagesRoot, name);
            try
            {
                using (var a = ZipFile.OpenRead(zip))
                {
                    if (!a.Entries.Any(e => string.Equals(Path.GetFileName(e.FullName), PlanName, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException(T.S("err.zipNoPlan"));
                    if (Directory.Exists(dest)) Directory.Delete(dest, true);
                    Directory.CreateDirectory(dest);
                    var root = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
                    foreach (var e in a.Entries)
                    {
                        var target = Path.GetFullPath(Path.Combine(dest, e.FullName.Replace('/', Path.DirectorySeparatorChar)));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // no ..\ escapes
                        if (e.FullName.EndsWith("/") || e.Name.Length == 0) { Directory.CreateDirectory(target); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        e.ExtractToFile(target, true);
                    }
                }
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception e) { throw new InvalidOperationException(T.S("err.zip", e.Message)); }
            Mod.Log.Info("unpacked " + zip + " to " + dest);
            return dest;
        }

        // ---------------------------------------------------------------- recent + found

        public static List<string> Recent(Setting s)
        {
            return (s.RecentPackages ?? "").Split('|').Where(x => x.Length > 0 && File.Exists(Path.Combine(x, PlanName))).ToList();
        }

        public static void Remember(Setting s, string folder)
        {
            var l = Recent(s);
            l.RemoveAll(x => string.Equals(x, folder, StringComparison.OrdinalIgnoreCase));
            l.Insert(0, folder);
            s.RecentPackages = string.Join("|", l.Take(MaxRecent).ToArray());
        }

        /// <summary>Folders with a cs1conv-plan.json under %LOCALAPPDATA%\cs1-to-cs2 (3 levels deep), newest first.</summary>
        public static List<string> Found()
        {
            var found = new List<string>();
            try { Walk(LocalRoot, 0, found); } catch (Exception e) { Mod.Log.Warn("package search failed: " + e.Message); }
            found.Sort((a, b) => File.GetLastWriteTimeUtc(Path.Combine(b, PlanName)).CompareTo(File.GetLastWriteTimeUtc(Path.Combine(a, PlanName))));
            return found;
        }

        static void Walk(string dir, int depth, List<string> found)
        {
            if (!Directory.Exists(dir) || depth > 3) return;
            if (File.Exists(Path.Combine(dir, PlanName))) { found.Add(dir); return; }
            foreach (var d in Directory.GetDirectories(dir)) Walk(d, depth + 1, found);
        }

        public static string Describe(string dir)
        {
            var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
            return name + "  (" + File.GetLastWriteTime(Path.Combine(dir, PlanName)).ToString("yyyy-MM-dd HH:mm") + ")";
        }

        // ---------------------------------------------------------------- Windows file dialog

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        sealed class OpenFileName
        {
            public int lStructSize = Marshal.SizeOf(typeof(OpenFileName));
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetOpenFileNameW([In, Out] OpenFileName ofn);

        [DllImport("comdlg32.dll")]
        static extern int CommDlgExtendedError();

        [DllImport("user32.dll")]
        static extern IntPtr GetActiveWindow();

        /// <summary>Title of the dialog; tests find the window by it.</summary>
        public static string DialogTitle { get { return T.S("pick.title"); } }

        /// <summary>
        /// Windows' Open dialog (modal over the game; the game waits while it is open). Returns the chosen file, or null
        /// when the user cancels. Throws when the dialog can't open (not Windows, no comdlg32).
        /// </summary>
        public static string BrowseFile(string initialDir)
        {
            const int OFN_HIDEREADONLY = 0x4, OFN_NOCHANGEDIR = 0x8, OFN_PATHMUSTEXIST = 0x800, OFN_FILEMUSTEXIST = 0x1000, OFN_EXPLORER = 0x80000;
            const int size = 4096;
            var buf = Marshal.AllocHGlobal(size * 2);
            try
            {
                Marshal.Copy(new byte[size * 2], 0, buf, size * 2);
                var ofn = new OpenFileName
                {
                    hwndOwner = GetActiveWindow(),
                    lpstrFilter = T.S("pick.filter") + " (" + PlanName + ", *.zip)\0" + PlanName + ";*.zip\0" + T.S("pick.all") + "\0*.*\0\0",
                    nFilterIndex = 1,
                    lpstrFile = buf,
                    nMaxFile = size,
                    lpstrInitialDir = initialDir,
                    lpstrTitle = DialogTitle,
                    Flags = OFN_HIDEREADONLY | OFN_NOCHANGEDIR | OFN_PATHMUSTEXIST | OFN_FILEMUSTEXIST | OFN_EXPLORER,
                };
                if (GetOpenFileNameW(ofn)) return Marshal.PtrToStringUni(buf);
                int err = CommDlgExtendedError();
                if (err != 0) throw new InvalidOperationException("comdlg32 error 0x" + err.ToString("X"));
                return null; // cancelled
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        /// <summary>Downloads if it exists (where browsers put the web tool's zip), else the packages folder.</summary>
        public static string InitialDir(Setting s)
        {
            var recent = Recent(s);
            if (recent.Count > 0) return Path.GetDirectoryName(recent[0]);
            var dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(dl)) return dl;
            Directory.CreateDirectory(PackagesRoot);
            return PackagesRoot;
        }
    }
}
