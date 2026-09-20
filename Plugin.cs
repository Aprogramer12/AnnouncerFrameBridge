using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using MainUI;

[assembly: AssemblyVersion("0.4.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETCoreApp,Version=v6.0")]
namespace AnnouncerFrameBridge
{
    [BepInPlugin("local.announcer.frame.bridge", "Announcer Frame Bridge Beta", "0.4.0")]
    public sealed class Plugin : BasePlugin
    {
        internal static ManualLogSource Report;
        internal static bool UseCache;
        private Harmony harmony;
        public override void Load()
        {
            Report = Log;
            UseCache = File.Exists(Path.Combine(Paths.PluginPath,"Lethe.dll"));
            if(!UseCache){Log.LogWarning("[AFB] Lethe.dll not found; local mode disabled. No hooks installed.");return;}
            using (var sha = SHA256.Create())
            {
                var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(Paths.GameRootPath,"GameAssembly.dll")))).Replace("-", "").ToLowerInvariant();
                if (hash != "e893ce931def3c55522021e127e0bddbd996cb55a54bf4852d23c18415863e08")
                { Log.LogWarning("[AFB] Unsupported game build. No hooks installed. SHA256=" + hash); return; }
            }
            harmony = new Harmony("local.announcer.frame.bridge");
            try
            {
                Hook(typeof(FormationUIPanel),"Initialize",Type.EmptyTypes,null,"PanelReady");
                Hook(typeof(FormationUIPanel),"SetDataOpen",new[]{typeof(UIPresenter),typeof(IFormationPanelData)},null,"PanelReady");
                Hook(typeof(FormationBattleAnnouncerUI),"OnAnnouncerButtonClicked",Type.EmptyTypes,"FrameClick",null);
                Hook(typeof(FormationUIPanel),"OnOpeningAnnouncerSelection",Type.EmptyTypes,"Opening",null);
                Hook(typeof(UserAnnouncerData),"UpdateAnnouncerPresetData",new[]{typeof(Il2CppSystem.Action)},"DataRequest",null);
                Hook(typeof(AnnouncerSelectionUI),"Open",Type.EmptyTypes,"SelectionOpening","SelectionOpened");
                Hook(typeof(UserAnnouncerData),"SendSelectedAnnouncerIdListToServer",new[]{typeof(int),typeof(Il2CppSystem.Collections.Generic.List<int>),typeof(Il2CppSystem.Action)},"SaveRequest",null);
                Hook(typeof(UserAnnouncerData),"UpdateData",new[]{typeof(Server.AnnouncerFormat)},null,"RestoreLocal");
                Hook(typeof(AnnouncerSelectionUI),"_Close_b__22_0",Type.EmptyTypes,null,"ClosedLocal");
                Hook(typeof(NetworkingUI),"PrintWarning",new[]{typeof(int),typeof(Server.HttpApiSchema),typeof(bool),typeof(bool),typeof(bool),typeof(DelegateEvent),typeof(Il2CppSystem.Collections.Generic.List<Server.StateCustomLocalizeText>)},"NetworkWarning",null);
                Log.LogInfo("[AFB] Beta 0.4.0 LOCAL MODE. Announcer presets saved on this PC; announcer preset read/write requests bypassed. No cloud sync.");
            }
            catch { harmony.UnpatchSelf(); throw; }
        }
        private void Hook(Type type,string name,Type[] args,string before,string after)
        {
            var original=AccessTools.Method(type,name,args);
            if(original==null)throw new MissingMethodException(type.FullName,name);
            harmony.Patch(original,before==null?null:new HarmonyMethod(typeof(Bridge).GetMethod(before)),
                after==null?null:new HarmonyMethod(typeof(Bridge).GetMethod(after)),null,
                new HarmonyMethod(typeof(Bridge).GetMethod("Failure")),null);
        }
    }
    public static class Bridge
    {
        private static int requestSerial;
        [ThreadStatic] private static int openingDepth;
        private static UserAnnouncerData localData;
        private static string StorePath { get { return Path.Combine(Paths.ConfigPath,"AnnouncerFrameBridge.local.json"); } }
        public sealed class LocalFile
        {
            public int Version {get;set;} = 1;
            public int Current {get;set;}
            public System.Collections.Generic.Dictionary<int,int[]> Presets {get;set;} = new System.Collections.Generic.Dictionary<int,int[]>();
        }
        private static LocalFile SnapshotLocal(UserAnnouncerData data)
        {
            var f=new LocalFile {Current=data.CurrentPresetId};
            for(int i=0;i<data.presetList.Count;i++)
            {
                var p=data.presetList[i];
                if(p==null || p.presetAnnouncerIds==null)continue;
                var ids=new int[p.presetAnnouncerIds.Count];
                for(int j=0;j<ids.Length;j++)ids[j]=p.presetAnnouncerIds[j];
                f.Presets[p.presetId]=ids;
            }
            return f;
        }
        private static void WriteLocal(LocalFile file)
        {
            Directory.CreateDirectory(Paths.ConfigPath);
            string temp=StorePath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(temp,System.Text.Json.JsonSerializer.Serialize(file));
                File.Move(temp,StorePath,true);
            }
            finally {if(File.Exists(temp))File.Delete(temp);}
        }
        private static bool ValidSelection(UserAnnouncerData data, int preset, int[] ids)
        {
            if(data==null || data.announcerIds==null || ids==null || preset<1 || preset>data.MAX_PRESET_COUNT
                || ids.Length>data.MAX_SELECTION_COUNT)return false;
            var seen=new System.Collections.Generic.HashSet<int>();
            foreach(int id in ids)if(!seen.Add(id)||!data.announcerIds.Contains(id))return false;
            return true;
        }
        public static void RestoreLocal(UserAnnouncerData __instance)
        {
            try
            {
                if(!Plugin.UseCache || __instance.announcerIds==null || __instance.presetList==null)return;
                localData=__instance;
                int max=__instance.MAX_PRESET_COUNT;
                if(max<1 || max>100)throw new InvalidDataException("Unexpected preset limit: "+max);
                LocalFile saved=null;
                if(File.Exists(StorePath))
                {
                    if(new FileInfo(StorePath).Length>65536)throw new InvalidDataException("Local preset file too large");
                    saved=System.Text.Json.JsonSerializer.Deserialize<LocalFile>(File.ReadAllText(StorePath));
                    if(saved==null || saved.Version!=1 || saved.Presets==null)throw new InvalidDataException("Invalid local preset file");
                }
                // Create ordinary empty local slots up to the game's own limit; ownership is unchanged.
                for(int id=1;id<=max;id++)
                {
                    bool exists=false;
                    for(int i=0;i<__instance.presetList.Count;i++)
                        if(__instance.presetList[i]!=null && __instance.presetList[i].presetId==id){exists=true;break;}
                    if(!exists)__instance.presetList.Add(new Server.AnnouncerPresetFormat(id,new Il2CppSystem.Collections.Generic.List<int>()));
                }
                if(saved==null)
                {
                    // KR_Announcer.json identifies the ordinary default Dante as ID 1.
                    // A missing local save must not inherit a different server-side selection.
                    if(!__instance.announcerIds.Contains(1))throw new InvalidDataException("Default Dante missing from owned data");
                    for(int id=1;id<=max;id++)
                    {
                        var defaults=new Il2CppSystem.Collections.Generic.List<int>();
                        defaults.Add(1);
                        __instance.SetAnnouncerPreset(id,defaults);
                    }
                    __instance.SetCurrentPresetId(1);
                    Plugin.Report.LogInfo("[AFB] No local save: default Dante selected in all presets.");
                }
                if(saved!=null)
                {
                    foreach(var entry in saved.Presets)
                    {
                        if(!ValidSelection(__instance,entry.Key,entry.Value))
                        {Plugin.Report.LogWarning("[AFB] Ignored invalid/unowned local preset "+entry.Key);continue;}
                        var list=new Il2CppSystem.Collections.Generic.List<int>();
                        foreach(int id in entry.Value)list.Add(id);
                        __instance.SetAnnouncerPreset(entry.Key,list);
                    }
                    if(saved.Current>=1 && saved.Current<=max)__instance.SetCurrentPresetId(saved.Current);
                }
                Plugin.Report.LogInfo("[AFB] LOCAL restored slots="+max+" current="+__instance.CurrentPresetId);
            }
            catch(Exception e){Plugin.Report.LogError("[AFB] local restore failed; file preserved: "+e);}
        }
        public static void ClosedLocal(AnnouncerSelectionUI __instance)
        {
            if(localData==null)return;
            try { WriteLocal(SnapshotLocal(localData));Plugin.Report.LogInfo("[AFB] LOCAL close complete; current preset="+localData.CurrentPresetId); }
            catch(Exception e){Plugin.Report.LogError("[AFB] could not persist current preset: "+e);}
        }
        public static void PanelReady(FormationUIPanel __instance)
        {
            try
            {
                var frame=__instance._leftAnnouncerUI;
                if(frame==null){Plugin.Report.LogWarning("[AFB] PANEL missing left frame; prefab incompatibility requires investigation.");return;}
                if(frame._onOpeningAnnouncerSelection==null)
                {
                    // Restore the exact owning panel's existing open path. Never open a global/other panel.
                    frame._onOpeningAnnouncerSelection=(System.Action)(()=>__instance.OnOpeningAnnouncerSelection());
                    Plugin.Report.LogWarning("[AFB] REPAIRED missing frame-to-owner callback.");
                }
                Plugin.Report.LogInfo("[AFB] PANEL frame="+frame.GetInstanceID()+" button="+(frame.btn_announcer!=null)
                    +" callback="+(frame._onOpeningAnnouncerSelection!=null)+" allowed="+frame._isAllowClickEvent
                    +" hidden="+frame._isHide+" restriction="+(frame._restrictData!=null));
            }
            catch(Exception e){Plugin.Report.LogError("[AFB] panel inspection: "+e);}
        }
        public static void FrameClick(FormationBattleAnnouncerUI __instance)
        {
            try {Plugin.Report.LogInfo("[AFB] CLICK frame="+__instance.GetInstanceID()+" allowed="+__instance._isAllowClickEvent
                +" callback="+(__instance._onOpeningAnnouncerSelection!=null)+" restriction="+(__instance._restrictData!=null));}
            catch(Exception e){Plugin.Report.LogError("[AFB] click inspection: "+e);}
        }
        public static void Opening(){openingDepth++;Plugin.Report.LogInfo("[AFB] OWNER opening requested");}
        private static bool ValidCache(UserAnnouncerData data)
        {
            if(data==null || data.announcerIds==null || data.presetList==null || data.presetList.Count==0)return false;
            bool current=false;
            var seen=new System.Collections.Generic.HashSet<int>();
            for(int i=0;i<data.presetList.Count;i++)
            {
                var p=data.presetList[i];
                if(p==null || !seen.Add(p.presetId) || p.presetAnnouncerIds==null)return false;
                if(p.presetAnnouncerIds.Count>data.MAX_SELECTION_COUNT)return false;
                for(int j=0;j<p.presetAnnouncerIds.Count;j++)
                    if(!data.announcerIds.Contains(p.presetAnnouncerIds[j]))return false;
                if(p.presetId==data.CurrentPresetId)current=true;
            }
            return current;
        }
        public static bool DataRequest(UserAnnouncerData __instance, ref Il2CppSystem.Action __0)
        {
            if(Plugin.UseCache)
            {
                RestoreLocal(__instance);
                if(!ValidCache(__instance))
                    { Plugin.Report.LogError("[AFB] Local data incomplete; opening canceled. No server request sent."); return false; }
                // This is a local-data completion callback, not a fabricated server response.
                if(__0!=null)__0.Invoke();
                return false;
            }
            bool useCached=false;
            try
            {
                int serial=++requestSerial;
                Plugin.Report.LogInfo("[AFB] DATA request="+serial+" refreshNeeded="+__instance._isChange
                    +" presets="+(__instance.presetList==null?-1:__instance.presetList.Count)
                    +" ownedCount="+(__instance.announcerIds==null?-1:__instance.announcerIds.Count)
                    +" callback="+(__0!=null));
                useCached=Plugin.UseCache && openingDepth>0 && __0!=null && ValidCache(__instance);
                if(useCached)
                    Plugin.Report.LogInfo("[AFB] CACHE validated; opening from existing preset. No refresh request sent; save remains server-backed.");
                if(__0==null)return true;
                if(useCached) { /* Invoke outside the inspection catch; a popup exception must not retry the request. */ }
                else {
                var original=__0;
                __0=(System.Action)(()=>{
                    Plugin.Report.LogInfo("[AFB] DATA callback="+serial);
                    original.Invoke();
                });
                }
            }
            catch(Exception e){Plugin.Report.LogError("[AFB] data inspection: "+e);}
            if(useCached){__0.Invoke();return false;}
            return true;
        }
        public static bool SaveRequest(UserAnnouncerData __instance, int __0, Il2CppSystem.Collections.Generic.List<int> __1, ref Il2CppSystem.Action __2)
        {
            if(Plugin.UseCache)
            {
                try
                {
                    if(__1==null)throw new InvalidDataException("Missing selection");
                    var ids=new int[__1.Count];
                    for(int i=0;i<ids.Length;i++)ids[i]=__1[i];
                    if(!ValidSelection(__instance,__0,ids))throw new InvalidDataException("Invalid or unowned selection");
                    var snapshot=SnapshotLocal(__instance);
                    snapshot.Presets[__0]=ids;
                    WriteLocal(snapshot); // Persist successfully before completing UI operation.
                    __instance.SetAnnouncerPreset(__0,__1);
                    localData=__instance;
                    Plugin.Report.LogInfo("[AFB] LOCAL saved preset="+__0+" count="+ids.Length+"; no server request");
                }
                catch(Exception e){Plugin.Report.LogError("[AFB] LOCAL save failed; operation not completed: "+e);return false;}
                if(__2!=null)__2.Invoke();
                return false;
            }
            Plugin.Report.LogInfo("[AFB] SAVE requested preset="+__0+" selectedCount="+(__1==null?-1:__1.Count));
            if(__2==null)return true;
            var original=__2;
            __2=(System.Action)(()=>{Plugin.Report.LogInfo("[AFB] SAVE callback received");original.Invoke();});
            return true;
        }
        public static void NetworkWarning(int __0)
        {Plugin.Report.LogWarning("[AFB] NETWORK warning code="+__0+" (original handler retained)");}
        public static void SelectionOpening(){Plugin.Report.LogInfo("[AFB] POPUP Open entered");}
        public static void SelectionOpened(AnnouncerSelectionUI __instance)
        {Plugin.Report.LogInfo("[AFB] POPUP Open returned active="+__instance.gameObject.activeInHierarchy);}
        public static Exception Failure(Exception __exception, MethodBase __originalMethod)
        {
            if(__originalMethod.DeclaringType==typeof(FormationUIPanel) && __originalMethod.Name=="OnOpeningAnnouncerSelection" && openingDepth>0)openingDepth--;
            if(__exception!=null)Plugin.Report.LogError("[AFB] "+__originalMethod.DeclaringType.Name+"."+__originalMethod.Name+": "+__exception);
            return __exception; // Retain original errors instead of hiding them.
        }
    }
}
