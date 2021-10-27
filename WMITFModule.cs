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

namespace WMITF
{
    public class WMITFModule : ETGModule
    {
        public override void Init()
        {
            WMITFEnabled = true;
            WMITFModItemDict = new Dictionary<PickupObject, Assembly>();
            new Hook(typeof(ItemDB).GetMethod("Add", new Type[] { typeof(PickupObject), typeof(bool), typeof(string) }), typeof(WMITFModule).GetMethod("WMITFAddItemToDict"));
            new Hook(typeof(PassiveItem).GetMethod("OnEnteredRange", BindingFlags.Public | BindingFlags.Instance), typeof(WMITFModule).GetMethod("WMITFShowModP"));
            new Hook(typeof(PassiveItem).GetMethod("OnExitRange", BindingFlags.Public | BindingFlags.Instance), typeof(WMITFModule).GetMethod("WMITFHideModP"));
            new Hook(typeof(PlayerItem).GetMethod("OnEnteredRange", BindingFlags.Public | BindingFlags.Instance), typeof(WMITFModule).GetMethod("WMITFShowModA"));
            new Hook(typeof(PlayerItem).GetMethod("OnExitRange", BindingFlags.Public | BindingFlags.Instance), typeof(WMITFModule).GetMethod("WMITFHideModA"));
            new Hook(typeof(Gun).GetMethod("OnEnteredRange", BindingFlags.Public | BindingFlags.Instance), typeof(WMITFModule).GetMethod("WMITFShowModG"));
            new Hook(typeof(Gun).GetMethod("OnExitRange", BindingFlags.Public | BindingFlags.Instance), typeof(WMITFModule).GetMethod("WMITFHideModG"));
            new Hook(typeof(RewardPedestal).GetMethod("OnEnteredRange", BindingFlags.Public | BindingFlags.Instance), typeof(WMITFModule).GetMethod("WMITFShowModR"));
            new Hook(typeof(RewardPedestal).GetMethod("OnExitRange", BindingFlags.Public | BindingFlags.Instance), typeof(WMITFModule).GetMethod("WMITFHideModR"));
            new Hook(typeof(EncounterDatabaseEntry).GetMethod("GetModifiedLongDescription", BindingFlags.Public | BindingFlags.Instance), typeof(WMITFModule).GetMethod("WMITFAddAmmonomiconModName"));
            WMITFRewardPedestalSpriteInfo = typeof(RewardPedestal).GetField("m_itemDisplaySprite", BindingFlags.NonPublic | BindingFlags.Instance);
            if(File.Exists(Path.Combine(ETGMod.GameFolder, "wmitfenabled.txt")))
            {
                if(bool.TryParse(File.ReadAllText(Path.Combine(ETGMod.GameFolder, "wmitfenabled.txt")), out bool bo))
                {
                    WMITFEnabled = bo;
                }
                else
                {
                    using (FileStream stream = File.Create(Path.Combine(ETGMod.GameFolder, "wmitfenabled.txt")))
                    {
                        byte[] b = Encoding.UTF8.GetBytes(bool.TrueString);
                        stream.Write(b, 0, b.Length);
                    }
                }
            }
            else
            {
                using (FileStream stream = File.Create(Path.Combine(ETGMod.GameFolder, "wmitfenabled.txt")))
                {
                    byte[] b = Encoding.UTF8.GetBytes(bool.TrueString);
                    stream.Write(b, 0, b.Length);
                }
            }
        }

        public static string WMITFAddAmmonomiconModName(Func<EncounterDatabaseEntry, string> orig, EncounterDatabaseEntry self)
        {
            string result = orig(self);
            if (WMITFActualModItemDict == null)
            {
                if (!WMITFGetActualModItemDict())
                {
                    return result;
                }
            }
            WMITFData match = GetMatch(self);
            if(match != null && WMITFActualModItemDict.ContainsKey(match) && WMITFActualModItemDict[match] != null && WMITFActualModItemDict[match].Metadata != null && !string.IsNullOrEmpty(WMITFActualModItemDict[match].Metadata.Name))
            {
                if (result.EndsWith("\n\n")) 
                {
                    result += "This item is from " + WMITFActualModItemDict[match].Metadata.Name;
                }
                else if (result.EndsWith("\n"))
                {
                    result += "\nThis item is from " + WMITFActualModItemDict[match].Metadata.Name;
                }
                else
                {
                    result += "\n\nThis item is from " + WMITFActualModItemDict[match].Metadata.Name;
                }
            }
            return result;
        }

        public override void Start()
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

        public static int WMITFAddItemToDict(Func<ItemDB, PickupObject, bool, string, int> orig, ItemDB self, PickupObject item, bool b, string floor)
        {
            int result = orig(self, item, b, floor);
            StackFrame[] frames = new StackTrace().GetFrames();
            int current = 1;
            while (frames[current].GetMethod().DeclaringType.Assembly == typeof(ETGMod).Assembly)
            {
                current++;
                if(current >= frames.Length)
                {
                    return result;
                }
            }
            WMITFModItemDict.Add(item, frames[current].GetMethod().DeclaringType.Assembly);
            if (WMITFFullyInited)
            {
                WMITFGetActualModItemDict();
            }
            return result;
        }

        public static void WMITFShowModP(Action<PassiveItem, PlayerController> orig, PassiveItem self, PlayerController player)
        {
            orig(self, player);
            WMITFShowMod(self);
        }

        public static void WMITFHideModP(Action<PassiveItem, PlayerController> orig, PassiveItem self, PlayerController player)
        {
            orig(self, player);
            WMITFHideMod(self);
        }

        public static void WMITFShowModA(Action<PlayerItem, PlayerController> orig, PlayerItem self, PlayerController player)
        {
            orig(self, player);
            WMITFShowMod(self);
        }

        public static void WMITFHideModA(Action<PlayerItem, PlayerController> orig, PlayerItem self, PlayerController player)
        {
            orig(self, player);
            WMITFHideMod(self);
        }

        public static void WMITFShowModG(Action<Gun, PlayerController> orig, Gun self, PlayerController player)
        {
            orig(self, player);
            WMITFShowMod(self);
        }

        public static void WMITFHideModG(Action<Gun, PlayerController> orig, Gun self, PlayerController player)
        {
            orig(self, player);
            WMITFHideMod(self);
        }

        public static void WMITFShowModR(Action<RewardPedestal, PlayerController> orig, RewardPedestal self, PlayerController player)
        {
            orig(self, player);
            if(self.contents != null)
            {
                if(WMITFRewardPedestalSpriteInfo == null)
                {
                    WMITFRewardPedestalSpriteInfo = typeof(RewardPedestal).GetField("m_itemDisplaySprite", BindingFlags.NonPublic | BindingFlags.Instance);
                }
                WMITFShowMod(self.contents, (tk2dBaseSprite)WMITFRewardPedestalSpriteInfo.GetValue(self));
            }
        }

        public static void WMITFHideModR(Action<RewardPedestal, PlayerController> orig, RewardPedestal self, PlayerController player)
        {
            orig(self, player);
            if (self.contents != null)
            {
                if (WMITFRewardPedestalSpriteInfo == null)
                {
                    WMITFRewardPedestalSpriteInfo = typeof(RewardPedestal).GetField("m_itemDisplaySprite", BindingFlags.NonPublic | BindingFlags.Instance);
                }
                WMITFHideMod(self.contents, ((tk2dBaseSprite)WMITFRewardPedestalSpriteInfo.GetValue(self)).transform);
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
            if (match != null && WMITFActualModItemDict.ContainsKey(match) && WMITFActualModItemDict[match] != null && WMITFActualModItemDict[match].Metadata != null && !string.IsNullOrEmpty(WMITFActualModItemDict[match].Metadata.Name))
            {
                tk2dBaseSprite s = overrideSprite ?? po.sprite;
                GameUIRoot.Instance.RegisterDefaultLabel(s.transform, new Vector3(s.GetBounds().max.x + 0.1875f, s.GetBounds().min.y, 0f), "This item is from " + WMITFActualModItemDict[match].Metadata.Name);
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
            WMITFActualModItemDict = new Dictionary<WMITFData, ETGModule>();
            foreach(KeyValuePair<PickupObject, Assembly> pair in WMITFModItemDict)
            {
                foreach(ETGModule module in ETGMod.AllMods)
                {
                    if(module.GetType().Assembly == pair.Value)
                    {
                        try
                        {
                            WMITFActualModItemDict.Add(new WMITFData { encounterOrDisplayName = pair.Key.EncounterNameOrDisplayName, objectName = pair.Key.name.Replace("(Clone)", ""), type = pair.Key.GetType(), baseItem = pair.Key, id = 
                                pair.Key.PickupObjectId}, module);
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
            using (FileStream stream = File.Create(Path.Combine(ETGMod.GameFolder, "wmitfenabled.txt")))
            {
                byte[] b = Encoding.UTF8.GetBytes(WMITFEnabled.ToString());
                stream.Write(b, 0, b.Length);
            }
        }

        public void WMITFState(string[] args)
        {
            ETGModConsole.Log("WMITF is currently " + (WMITFEnabled ? "enabled" : "disabled") + ".");
        }

        public override void Exit()
        {
        }

        public static bool WMITFEnabled;
        public static bool WMITFFullyInited;
        public static Dictionary<PickupObject, Assembly> WMITFModItemDict;
        public static Dictionary<WMITFData, ETGModule> WMITFActualModItemDict;
        public static FieldInfo WMITFRewardPedestalSpriteInfo;
    }
}
