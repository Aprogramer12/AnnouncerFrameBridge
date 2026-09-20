$ErrorActionPreference='Stop'
$src=Get-Content (Join-Path $PSScriptRoot 'Plugin.cs') -Raw
$a=$src.IndexOf('        private static UserAnnouncerData localData;')
$b=$src.IndexOf('        public static void PanelReady(',$a)
$part=$src.Substring($a,$b-$a)
$a=$src.IndexOf('        public static bool SaveRequest(')
$b=$src.IndexOf('        public static void NetworkWarning(',$a)
$part+=$src.Substring($a,$b-$a)
$head=@'
using System;
using System.IO;
public static class Paths {public static string ConfigPath;}
public class Logger {public void LogInfo(object x){} public void LogWarning(object x){} public void LogError(object x){}}
public static class Plugin {public static bool UseCache=true;public static Logger Report=new Logger();}
namespace Il2CppSystem {public class Action {System.Action x;public Action(System.Action a){x=a;}public void Invoke(){x();}public static implicit operator Action(System.Action a){return new Action(a);}}}
namespace Il2CppSystem.Collections.Generic {public class List<T>:System.Collections.Generic.List<T>{}}
namespace Server {public class AnnouncerPresetFormat {public int presetId;public Il2CppSystem.Collections.Generic.List<int> presetAnnouncerIds;public AnnouncerPresetFormat(int id,Il2CppSystem.Collections.Generic.List<int> ids){presetId=id;presetAnnouncerIds=ids;}}}
public class AnnouncerSelectionUI {}
public class UserAnnouncerData {
 public Il2CppSystem.Collections.Generic.List<int> announcerIds=new Il2CppSystem.Collections.Generic.List<int>{1,7,8};
 public Il2CppSystem.Collections.Generic.List<Server.AnnouncerPresetFormat> presetList=new Il2CppSystem.Collections.Generic.List<Server.AnnouncerPresetFormat>();
 public int MAX_PRESET_COUNT=3, MAX_SELECTION_COUNT=2,CurrentPresetId=1;
 public void SetCurrentPresetId(int id){CurrentPresetId=id;}
 public void SetAnnouncerPreset(int id,Il2CppSystem.Collections.Generic.List<int> v){foreach(var p in presetList)if(p.presetId==id){p.presetAnnouncerIds=v;return;}throw new Exception("missing preset");}
}
public static class LocalRegression {
'@
$tail=@'
 public static void Test(string root) {
  Paths.ConfigPath=root;
  var d=new UserAnnouncerData();RestoreLocal(d);
  if(d.presetList.Count!=3)throw new Exception("slots missing");
  foreach(var p in d.presetList)if(p.presetAnnouncerIds.Count!=1||p.presetAnnouncerIds[0]!=1)throw new Exception("Dante default missing");
  if(d.CurrentPresetId!=1)throw new Exception("default preset incorrect");
  int calls=0;Il2CppSystem.Action cb=(System.Action)(()=>calls++);
  var one=new Il2CppSystem.Collections.Generic.List<int>{7};
  var two=new Il2CppSystem.Collections.Generic.List<int>{8};
  if(SaveRequest(d,1,one,ref cb)||SaveRequest(d,2,two,ref cb)||calls!=2)throw new Exception("local callbacks/request suppression");
  d.SetCurrentPresetId(2);ClosedLocal(new AnnouncerSelectionUI());
  var reloaded=new UserAnnouncerData();RestoreLocal(reloaded);
  if(reloaded.CurrentPresetId!=2||reloaded.presetList[0].presetAnnouncerIds[0]!=7||reloaded.presetList[1].presetAnnouncerIds[0]!=8)throw new Exception("restart persistence");
  string before=File.ReadAllText(StorePath);
  var bad=new Il2CppSystem.Collections.Generic.List<int>{99};SaveRequest(reloaded,1,bad,ref cb);
  var dup=new Il2CppSystem.Collections.Generic.List<int>{7,7};SaveRequest(reloaded,1,dup,ref cb);
  SaveRequest(reloaded,4,one,ref cb);
  if(calls!=2||before!=File.ReadAllText(StorePath))throw new Exception("invalid save accepted");
  string blocker=Path.Combine(root,"not-a-directory");File.WriteAllText(blocker,"sentinel");Paths.ConfigPath=blocker;
  SaveRequest(reloaded,1,two,ref cb);
  if(calls!=2||reloaded.presetList[0].presetAnnouncerIds[0]!=7)throw new Exception("failed write completed operation");
  Paths.ConfigPath=root;
  reloaded.announcerIds.Remove(8);RestoreLocal(reloaded);
  // No new ownership can be created by loading saved selections.
  if(reloaded.announcerIds.Contains(8))throw new Exception("ownership modified");
 }
}
'@
Add-Type -TypeDefinition ($head+$part+$tail)
$root=Join-Path $PSScriptRoot ('local-tests-'+[guid]::NewGuid().ToString('N'))
[LocalRegression]::Test($root)
'PASS: actual local save/restore source; independent presets, current preset restart persistence, callbacks, no server dispatch, invalid/unowned/duplicate rejection, write-failure behavior. Engine types mocked.'
