# Marks a local Cities: Skylines 1 mod as enabled, which is what ticking it in Content Manager does.
# CS1 keeps new local mods disabled. The flag is the bool "<ModFolderName><hash>.enabled" in
# %LOCALAPPDATA%\Colossal Order\Cities_Skylines\userGameState.cgs, where <hash> is Mono 2's String.GetHashCode() of the
# mod folder path (PluginManager.PluginInfo.isEnabled). The file is a plain BinaryWriter dump (ColossalFramework.SettingsFile).
#
#   powershell -ExecutionPolicy Bypass -File tools\cs1-bridge\enable-mod.ps1            # check only
#   powershell -ExecutionPolicy Bypass -File tools\cs1-bridge\enable-mod.ps1 -Write     # enable (backs up the file once)
#
# The game must not be running: it rewrites the file on exit.
param([string]$ModName = 'Cs1Bridge', [switch]$Write)
$ErrorActionPreference = 'Stop'
if (Get-Process Cities -ErrorAction SilentlyContinue) { throw 'Cities.exe is running; quit it first (it rewrites userGameState.cgs on exit)' }

Add-Type -TypeDefinition @"
using System; using System.IO; using System.Text; using System.Collections.Generic;
public static class Cs1Cgs {
  // Mono 2.x String.GetHashCode (differs from .NET's).
  public static int MonoHash(string s) { unchecked { int h = 0, i = 0, n = s.Length; for (; i + 1 < n; i += 2) { h = (h << 5) - h + s[i]; h = (h << 5) - h + s[i + 1]; } if (i < n) h = (h << 5) - h + s[i]; return h; } }

  public static string Run(string path, string key, bool write) {
    var bytes = File.ReadAllBytes(path);
    var r = new BinaryReader(new MemoryStream(bytes));
    var magic = r.ReadChars(4); if (new string(magic) != "CGSF") throw new Exception("not a CGSF settings file");
    ushort ver = r.ReadUInt16();
    var ints = new List<KeyValuePair<string,int>>(); int n = r.ReadInt32(); for (int i = 0; i < n; i++) ints.Add(new KeyValuePair<string,int>(r.ReadString(), r.ReadInt32()));
    var bools = new List<KeyValuePair<string,bool>>(); n = r.ReadInt32(); for (int i = 0; i < n; i++) bools.Add(new KeyValuePair<string,bool>(r.ReadString(), r.ReadBoolean()));
    var floats = new List<KeyValuePair<string,float>>(); n = r.ReadInt32(); for (int i = 0; i < n; i++) floats.Add(new KeyValuePair<string,float>(r.ReadString(), r.ReadSingle()));
    var strs = new List<KeyValuePair<string,string>>(); n = r.ReadInt32(); for (int i = 0; i < n; i++) strs.Add(new KeyValuePair<string,string>(r.ReadString(), r.ReadString()));
    var keys = new List<KeyValuePair<string,int>>(); n = r.ReadInt32(); for (int i = 0; i < n; i++) keys.Add(new KeyValuePair<string,int>(r.ReadString(), r.ReadInt32()));
    var longs = new List<KeyValuePair<string,long>>(); n = r.ReadInt32(); for (int i = 0; i < n; i++) longs.Add(new KeyValuePair<string,long>(r.ReadString(), r.ReadInt64()));
    if (r.BaseStream.Position != bytes.Length) throw new Exception("unexpected trailing bytes; format changed?");
    Func<byte[]> serialize = () => {
      var ms = new MemoryStream(); var w = new BinaryWriter(ms); w.Write(magic); w.Write(ver);
      w.Write(ints.Count); foreach (var kv in ints) { w.Write(kv.Key); w.Write(kv.Value); }
      w.Write(bools.Count); foreach (var kv in bools) { w.Write(kv.Key); w.Write(kv.Value); }
      w.Write(floats.Count); foreach (var kv in floats) { w.Write(kv.Key); w.Write(kv.Value); }
      w.Write(strs.Count); foreach (var kv in strs) { w.Write(kv.Key); w.Write(kv.Value); }
      w.Write(keys.Count); foreach (var kv in keys) { w.Write(kv.Key); w.Write(kv.Value); }
      w.Write(longs.Count); foreach (var kv in longs) { w.Write(kv.Key); w.Write(kv.Value); }
      w.Flush(); return ms.ToArray(); };
    if (Convert.ToBase64String(serialize()) != Convert.ToBase64String(bytes)) throw new Exception("round trip differs; refusing to write");
    int idx = bools.FindIndex(kv => kv.Key == key);
    var msg = idx >= 0 ? key + " = " + bools[idx].Value : key + " is not set (mod disabled)";
    if (!write) return msg;
    if (idx >= 0 && bools[idx].Value) return msg + " (nothing to do)";
    if (idx >= 0) bools[idx] = new KeyValuePair<string,bool>(key, true); else bools.Add(new KeyValuePair<string,bool>(key, true));
    File.WriteAllBytes(path + ".tmp", serialize());
    File.Copy(path + ".tmp", path, true);
    File.Delete(path + ".tmp");
    return key + " = True (written)";
  }
}
"@

$cs1 = Join-Path $env:LOCALAPPDATA 'Colossal Order\Cities_Skylines'
$cgs = Join-Path $cs1 'userGameState.cgs'
$modPath = Join-Path $cs1 "Addons\Mods\$ModName"
if (-not (Test-Path $modPath)) { Write-Warning "$modPath does not exist yet (build the mod with -c Release first)" }
$key = $ModName + [Cs1Cgs]::MonoHash($modPath) + '.enabled'
if ($Write) {
    $backup = "$cgs.bak-enable-mod"
    if (-not (Test-Path $backup)) { Copy-Item $cgs $backup; "backup: $backup" }
}
[Cs1Cgs]::Run($cgs, $key, [bool]$Write)
