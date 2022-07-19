using MonoMod.RuntimeDetour;
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;
using System.Diagnostics;
using System.Collections;
using System.IO;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace WMITF
{
    [HarmonyPatch]
    [BepInDependency("etgmodding.etg.mtgapi")]
    [BepInPlugin(GUID, NAME, VERSION)]
    public class WMITFModule : BaseUnityPlugin
    {
        public const string GUID = "spapi.etg.wmitf";
        public const string NAME = "What Mod Is This From (WMITF)";
        public const string VERSION = "1.0.1";

        public void Awake()
        {
            WMITFEnabled = true;
            WMITFModItemDict = new Dictionary<PickupObject, Assembly>();
            new Harmony(GUID).PatchAll();
            if(File.Exists(Path.Combine(Paths.GameRootPath, "wmitfenabled.txt")))
            {
                if(bool.TryParse(File.ReadAllText(Path.Combine(Paths.GameRootPath, "wmitfenabled.txt")), out bool bo))
                {
                    WMITFEnabled = bo;
                }
                else
                {
                    using (FileStream stream = File.Create(Path.Combine(Paths.GameRootPath, "wmitfenabled.txt")))
                    {
                        byte[] b = Encoding.UTF8.GetBytes(bool.TrueString);
                        stream.Write(b, 0, b.Length);
                    }
                }
            }
            else
            {
                using (FileStream stream = File.Create(Path.Combine(Paths.GameRootPath, "wmitfenabled.txt")))
                {
                    byte[] b = Encoding.UTF8.GetBytes(bool.TrueString);
                    stream.Write(b, 0, b.Length);
                }
            }
        }

        [HarmonyPatch(typeof(EncounterDatabaseEntry), nameof(EncounterDatabaseEntry.GetModifiedLongDescription))]
        [HarmonyPostfix]
        public static void WMITFAddAmmonomiconModName(EncounterDatabaseEntry __instance, ref string __result)
        {
            if (WMITFActualModItemDict == null)
            {
                if (!WMITFGetActualModItemDict())
                {
                    return;
                }
            }
            WMITFData match = GetMatch(__instance);
            if(match != null && WMITFActualModItemDict.ContainsKey(match) && WMITFActualModItemDict[match]?.Info?.Metadata != null && !string.IsNullOrEmpty(WMITFActualModItemDict[match].Info.Metadata.Name))
            {
                if (__result.EndsWith("\n\n")) 
                {
                    __result += "This item is from " + WMITFActualModItemDict[match].Info.Metadata.Name;
                }
                else if (__result.EndsWith("\n"))
                {
                    __result += "\nThis item is from " + WMITFActualModItemDict[match].Info.Metadata.Name;
                }
                else
                {
                    __result += "\n\nThis item is from " + WMITFActualModItemDict[match].Info.Metadata.Name;
                }
            }
        }

        public void Start()
        {
            ETGModMainBehaviour.WaitForGameManagerStart(GMStart);
        }

        public void GMStart(GameManager manager)
        {
            ETGModConsole.Commands.AddGroup("wmitf");
            ETGModConsole.Commands.GetGroup("wmitf").AddUnit("help", WMITFHelp).AddUnit("toggle", WMITFToggle).AddUnit("refresh", WMITFRefresh).AddUnit("state", WMITFState);
            ETGMod.StartGlobalCoroutine(DelayedDictionaryInit());
            ETGModConsole.Log("WMITF (What Mod Is This From) successfully initialized.");
        }

        public IEnumerator DelayedDictionaryInit()
        {
            yield return null;
            WMITFFullyInited = true;
            WMITFGetActualModItemDict();
        }

        public static void WMITFRefresh(string[] args)
        {
            if (WMITFGetActualModItemDict())
            {
                ETGModConsole.Log("Successfully refreshed WMITF mod item database.");
            }
            else
            {
                ETGModConsole.Log("Failed refreshing WMITF mod item databse.");
            }
        }

        [HarmonyPatch(typeof(ItemDB), nameof(ItemDB.Add), typeof(PickupObject), typeof(bool), typeof(string))]
        [HarmonyPostfix]
        public static void WMITFAddItemToDict(PickupObject value)
        {
            StackFrame[] frames = new StackTrace().GetFrames();
            int current = 1;
            while (frames[current].GetMethod().DeclaringType.Assembly == typeof(ETGMod).Assembly || frames[current].GetMethod().DeclaringType.Assembly == typeof(Harmony).Assembly ||
                frames[current].GetMethod().DeclaringType.Assembly == typeof(Hook).Assembly)
            {
                current++;
                if(current >= frames.Length)
                {
                    return;
                }
            }
            if (!WMITFModItemDict.ContainsKey(value))
            {
                WMITFModItemDict.Add(value, frames[current].GetMethod().DeclaringType.Assembly);
            }
            if (WMITFFullyInited)
            {
                WMITFGetActualModItemDict();
            }
        }

        [HarmonyPatch(typeof(PassiveItem), nameof(PassiveItem.OnEnteredRange))]
        [HarmonyPostfix]
        public static void WMITFShowModP(PassiveItem __instance)
        {
            WMITFShowMod(__instance);
        }

        [HarmonyPatch(typeof(PassiveItem), nameof(PassiveItem.OnExitRange))]
        [HarmonyPostfix]
        public static void WMITFHideModP(PassiveItem __instance)
        {
            WMITFHideMod(__instance);
        }

        [HarmonyPatch(typeof(PlayerItem), nameof(PlayerItem.OnEnteredRange))]
        [HarmonyPostfix]
        public static void WMITFShowModA(PlayerItem __instance)
        {
            WMITFShowMod(__instance);
        }

        [HarmonyPatch(typeof(PlayerItem), nameof(PlayerItem.OnExitRange))]
        [HarmonyPostfix]
        public static void WMITFHideModA(PlayerItem __instance)
        {
            WMITFHideMod(__instance);
        }

        [HarmonyPatch(typeof(Gun), nameof(Gun.OnEnteredRange))]
        [HarmonyPostfix]
        public static void WMITFShowModG(Gun __instance)
        {
            WMITFShowMod(__instance);
        }

        [HarmonyPatch(typeof(Gun), nameof(Gun.OnExitRange))]
        [HarmonyPatch(typeof(Gun), nameof(Gun.Interact))]
        [HarmonyPostfix]
        public static void WMITFHideModG(Gun __instance)
        {
            WMITFHideMod(__instance);
        }

        [HarmonyPatch(typeof(PickupObject), nameof(PickupObject.OnDestroy))]
        [HarmonyPostfix]
        public static void WMITFHideModP(PickupObject __instance)
        {
            WMITFHideMod(__instance);
        }

        [HarmonyPatch(typeof(RewardPedestal), nameof(RewardPedestal.OnEnteredRange))]
        [HarmonyPostfix]
        public static void WMITFShowModR(RewardPedestal __instance)
        {
            if(__instance.contents != null)
            {
                WMITFShowMod(__instance.contents, __instance.m_itemDisplaySprite);
            }
        }

        [HarmonyPatch(typeof(RewardPedestal), nameof(RewardPedestal.OnExitRange))]
        [HarmonyPostfix]
        public static void WMITFHideModR(RewardPedestal __instance)
        {
            if (__instance.contents != null)
            {
                WMITFHideMod(__instance.contents, __instance.m_itemDisplaySprite.transform);
            }
        }

        public static void WMITFShowMod(PickupObject po, tk2dBaseSprite overrideSprite = null)
        {
            if(WMITFActualModItemDict == null)
            {
                if (!WMITFGetActualModItemDict())
                {
                    return;
                }
            }
            if (!WMITFEnabled)
            {
                return;
            }
            WMITFData match = GetMatch(po);
            if (match != null && WMITFActualModItemDict.ContainsKey(match) && WMITFActualModItemDict[match]?.Info?.Metadata != null && !string.IsNullOrEmpty(WMITFActualModItemDict[match].Info.Metadata.Name))
            {
                tk2dBaseSprite s = overrideSprite ?? po.sprite;
                GameUIRoot.Instance.RegisterDefaultLabel(s.transform, new Vector3(s.GetBounds().max.x + 0.1875f, s.GetBounds().min.y, 0f), "This item is from " + WMITFActualModItemDict[match].Info.Metadata.Name);
            }
        }

        public static void WMITFHideMod(PickupObject po, Transform overrideTransform = null)
        {
            GameUIRoot.Instance.DeregisterDefaultLabel(overrideTransform ?? po.transform);
        }

        public static bool WMITFGetActualModItemDict()
        {
            if(WMITFModItemDict == null)
            {
                ETGModConsole.Log("[WMITF] Error! Tried getting actual mod item dict when normal mod item dict is null!");
                return false;
            }
            WMITFActualModItemDict = new Dictionary<WMITFData, BaseUnityPlugin>();
            foreach(KeyValuePair<PickupObject, Assembly> pair in WMITFModItemDict)
            {
                foreach(PluginInfo module in Chainloader.PluginInfos.Values)
                {
                    if(module.Instance.GetType().Assembly == pair.Value)
                    {
                        try
                        {
                            WMITFActualModItemDict.Add(new WMITFData { encounterOrDisplayName = pair.Key.EncounterNameOrDisplayName, objectName = pair.Key.name.Replace("(Clone)", ""), type = pair.Key.GetType(), baseItem = pair.Key, id = 
                                pair.Key.PickupObjectId}, module.Instance);
                        }
                        catch { }
                        break;
                    }
                }
            }
            return true;
        }

        public static WMITFData GetMatch(PickupObject po)
        {
            if (WMITFActualModItemDict == null)
            {
                if (!WMITFGetActualModItemDict())
                {
                    return null;
                }
            }
            foreach(WMITFData data in WMITFActualModItemDict.Keys)
            {
                if(data.encounterOrDisplayName == po.EncounterNameOrDisplayName && data.objectName == po.name.Replace("(Clone)", "") && data.type == po.GetType() && data.id == po.PickupObjectId)
                {
                    return data;
                }
            }
            return null;
        }

        public static WMITFData GetMatch(EncounterDatabaseEntry e)
        {
            if (WMITFActualModItemDict == null)
            {
                if (!WMITFGetActualModItemDict())
                {
                    return null;
                }
            }
            foreach (WMITFData data in WMITFActualModItemDict.Keys)
            {
                if (e.pickupObjectId == data.id)
                {
                    return data;
                }
            }
            return null;
        }

        public void WMITFHelp(string[] args)
        {
            ETGModConsole.Log("wmitf help - shows this help.");
            ETGModConsole.Log("wmitf toggle - toggles WMITF mod display. Does not remove the mod info from the end of the item's ammonomicon description.");
            ETGModConsole.Log("wmitf refresh - refreshes WMITF mod item database. Use this command if a modded item isn't included in WMITF.");
        }

        public void WMITFToggle(string[] args)
        {
            WMITFEnabled = !WMITFEnabled;
            ETGModConsole.Log("WMITF is currently " + (WMITFEnabled ? "enabled" : "disabled") + ".");
            using (FileStream stream = File.Create(Path.Combine(Paths.GameRootPath, "wmitfenabled.txt")))
            {
                byte[] b = Encoding.UTF8.GetBytes(WMITFEnabled.ToString());
                stream.Write(b, 0, b.Length);
            }
        }

        public void WMITFState(string[] args)
        {
            ETGModConsole.Log("WMITF is currently " + (WMITFEnabled ? "enabled" : "disabled") + ".");
        }

        public static bool WMITFEnabled;
        public static bool WMITFFullyInited;
        public static Dictionary<PickupObject, Assembly> WMITFModItemDict;
        public static Dictionary<WMITFData, BaseUnityPlugin> WMITFActualModItemDict;
    }
}
