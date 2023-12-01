using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PersistentPurchases;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Pool;

namespace PersistentPurchases
{
    [BepInPlugin("beeisyou.PersistentPurchases", "Persistent Purchases", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        private static ConfigEntry<bool> resetSuits;
        private static ConfigEntry<bool> resetFurniture;
        private static ConfigEntry<bool> resetUpgrades;
        private static ConfigEntry<bool> defaultPlacement;

        public static ManualLogSource log = new ManualLogSource("Persistent Purchases");
        public static Harmony harmony = new Harmony("beeisyou.PersistentPurchases");
        private void Awake()
        {
            BepInEx.Logging.Logger.Sources.Add(log);

            resetSuits = Config.Bind("Resetting", "ResetSuits", false, "Remove all but the orange suit on game over");
            resetFurniture = Config.Bind("Resetting", "ResetFurniture", false, "Remove cosmetic purchases on game over");
            resetUpgrades = Config.Bind("Resetting", "ResetUpgrades", true, "Remove ship upgrades on game over");
            defaultPlacement = Config.Bind("Resetting", "DefaultPlacement", true, "Reset objects to their default position, and put everything else in storage");

            harmony.PatchAll(typeof(Patches));
            harmony.PatchAll(typeof(ResetSavedGameValues));
            harmony.PatchAll(typeof(ResetShip));
            harmony.PatchAll(typeof(InSanityCheck));

            log.LogMessage("Plugin Persistent Purchases is loaded!");
        }

        public static int[] suits = { 0, 1, 2, 3 }; // orange, green, yellow, pajamas
        public static int[] upgrades = { 5, 18, 19 }; // teleporter, horn, inv teleporter
        public static int[] defaults = { 7, 8, 11, 15, 16 }; // cupboard, cabinet, light switch, bunkbeds, terminal

        public static bool shouldReset(int id, string debugType="")
        {
            if (suits.Contains(id))
            {
                if (!resetSuits.Value)
                {
                    log.LogInfo($"Not resetting {id} {debugType}");
                    return false;
                }
            }
            else if (upgrades.Contains(id))
            {
                if (!resetUpgrades.Value) {
                    log.LogInfo($"Not resetting {id} {debugType}");
                    return false;
                }
            }
            else if (!resetFurniture.Value)
            {
                log.LogInfo($"Not resetting {id} {debugType}");
                return false;
            }
            if (defaults.Contains(id))
            {
                log.LogInfo($"Not resetting {id} {debugType}");
                return false;
            }
            log.LogInfo($"Is resetting {id} {debugType}");
            return true;
        }

        public static bool shouldDefault()
        {
            return defaultPlacement.Value;
        }
    }
}

[HarmonyPatch]
public class Patches
{
    [HarmonyPrefix, HarmonyPatch(typeof(GameNetworkManager), nameof(GameNetworkManager.ResetUnlockablesListValues))]
    public static bool dontResetAnything()
    {
        if (StartOfRound.Instance != null)
        {
            UnityEngine.Debug.Log("CONDITIONALLY resetting unlockables list!");
            List<UnlockableItem> list = StartOfRound.Instance.unlockablesList.unlockables;
            for (int i = 0; i < list.Count; i++)
            {
                if (Plugin.shouldReset(i, "dontResetAnything")) // the only modified line lol
                {
                    Plugin.log.LogInfo($"Resetting {list[i].unlockableName}");
                    list[i].hasBeenUnlockedByPlayer = false;
                    if (list[i].unlockableType == 1)
                    {
                        list[i].placedPosition = Vector3.zero;
                        list[i].placedRotation = Vector3.zero;
                        list[i].hasBeenMoved = false;
                        list[i].inStorage = false;
                    }
                }
            }
        }
        return false; // skip original function
    }


    [HarmonyPostfix, HarmonyPatch(typeof(GameNetworkManager), nameof(GameNetworkManager.ResetSavedGameValues))]
    public static void okayMaybeDoALittleResetting()
    {
        if (ES3.KeyExists("UnlockedShipObjects", GameNetworkManager.Instance.currentSaveFileName))
        {
            int[] arr = ES3.Load<int[]>("UnlockedShipObjects", GameNetworkManager.Instance.currentSaveFileName);
            arr = arr.Where(id => !Plugin.shouldReset(id, "okayMaybeDoALittleResetting1")).ToArray(); // only keep items that should not be reset
            ES3.Save("UnlockedShipObjects", arr, GameNetworkManager.Instance.currentSaveFileName);

            for (int i = 0; i < StartOfRound.Instance.unlockablesList.unlockables.Count; i++)
            {
                UnlockableItem item = StartOfRound.Instance.unlockablesList.unlockables[i];
                if (Plugin.shouldReset(i, "okayMaybeDoALittleResetting2") && item.unlockableType == 1)
                {
                    ES3.DeleteKey("ShipUnlockMoved_" + item.unlockableName, GameNetworkManager.Instance.currentSaveFileName);
                    ES3.DeleteKey("ShipUnlockStored_" + item.unlockableName, GameNetworkManager.Instance.currentSaveFileName);
                    ES3.DeleteKey("ShipUnlockPos_" + item.unlockableName, GameNetworkManager.Instance.currentSaveFileName);
                    ES3.DeleteKey("ShipUnlockRot_" + item.unlockableName, GameNetworkManager.Instance.currentSaveFileName);
                }
            }
        }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.ResetShip))]
    public static void unfuckObjects()
    {
        PlaceableShipObject[] array = UnityEngine.Object.FindObjectsOfType<PlaceableShipObject>();
        for (int i = 0; i < array.Length; i++)
        {
            UnlockableItem item = StartOfRound.Instance.unlockablesList.unlockables[array[i].unlockableID];
            if (item.spawnPrefab)
            {
                if (item.hasBeenUnlockedByPlayer && !Plugin.shouldReset(array[i].unlockableID))
                {
                    if (Plugin.shouldDefault())
                    {
                        item.inStorage = true;
                    }
                }
                else
                {
                    item.hasBeenUnlockedByPlayer = false;
                    item.inStorage = false;
                }
                Collider[] componentsInChildren = array[i].parentObject.GetComponentsInChildren<Collider>();
                for (int j = 0; j < componentsInChildren.Length; j++)
                {
                    componentsInChildren[j].enabled = true;
                }
            }
            else if(Plugin.shouldDefault())
            {
                if (item.alreadyUnlocked)
                {
                    item.inStorage = false;
                    array[i].parentObject.disableObject = false;
                    ShipBuildModeManager.Instance.ResetShipObjectToDefaultPosition(array[i]);
                }
                else
                {
                    if (Plugin.shouldReset(array[i].unlockableID))
                    {
                        item.hasBeenUnlockedByPlayer = false;
                        item.inStorage = false;
                    } 
                    else
                    {
                        item.hasBeenUnlockedByPlayer = true;
                        item.inStorage = true;
                    }
                    array[i].parentObject.disableObject = true;
                    ShipBuildModeManager.Instance.StoreObjectServerRpc(
                        array[i].parentObject.GetComponent<NetworkObject>(), 
                        (int)GameNetworkManager.Instance.localPlayerController.playerClientId
                    );
                }
            }
        }

        for (int i = 0; i < StartOfRound.Instance.unlockablesList.unlockables.Count; i++)
        {
            UnlockableItem item = StartOfRound.Instance.unlockablesList.unlockables[i];
            if (!item.alreadyUnlocked && item.spawnPrefab && Plugin.shouldReset(i))
            {
                GameObject gameObject;
                if (!StartOfRound.Instance.SpawnedShipUnlockables.TryGetValue(i, out gameObject))
                {
                    StartOfRound.Instance.SpawnedShipUnlockables.Remove(i);
                }
                else if (gameObject == null)
                {
                    StartOfRound.Instance.SpawnedShipUnlockables.Remove(i);
                }
                else
                {
                    StartOfRound.Instance.SpawnedShipUnlockables.Remove(i);
                    NetworkObject component = gameObject.GetComponent<NetworkObject>();
                    if (component != null && component.IsSpawned)
                    {
                        component.Despawn(true);
                    }
                }
            }
        }
    }
}

[HarmonyPatch(typeof(GameNetworkManager), nameof(GameNetworkManager.ResetSavedGameValues))]
public class ResetSavedGameValues {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        Plugin.log.LogWarning("Beginning transpilation of GameNetworkManager.ResetSavedGameValues()");
        var codes = new List<CodeInstruction>(instructions);
        int rm = -1;
        List<int> rm2 = new List<int>();

        for (var i = 0; i < codes.Count; i++)
        {
            if (codes[i].opcode == OpCodes.Ldstr)
            {
                if (codes[i].operand.ToString() == "UnlockedShipObjects")
                {
                    rm = i;
                    Plugin.log.LogInfo($"Found ship object opcode at {i}");
                }
                else if (codes[i].operand.ToString().EndsWith("_"))
                {
                    rm2.Add(i);
                    Plugin.log.LogInfo($"Found placeable ship object data opcode at {i}");
                }
            }
        }
        for (int i = rm2.Count - 1; i >= 0; i--)
        {
            /*
             *     REMOVES:
             * ldstr     [string name_]
             * ldloc.1
             * ldfld     class UnlockablesList StartOfRound::unlockablesList
             * ldfld     class [netstandard]System.Collections.Generic.List`1<class UnlockableItem> UnlockablesList::unlockables
             * ldloc.2
             * callvirt  instance !0 class [netstandard]System.Collections.Generic.List`1<class UnlockableItem>::get_Item(int32)
             * ldfld     string UnlockableItem::unlockableName
             * call      string [netstandard]System.String::Concat(string, string)
             * ldarg.0
             * ldfld     string GameNetworkManager::currentSaveFileName
             * call      void ['Assembly-CSharp-firstpass']ES3::DeleteKey(string, string)
             *     AKA:
             * ES3.DeleteKey([string name_] + startOfRound.unlockablesList.unlockables[i].unlockableName, this.currentSaveFileName);
             */
            codes.RemoveRange(rm2[i], 11);
        }
        if (rm >= 0)
        {
            /*
            *     REMOVES:
            * ldstr     [string name]
            * call      class GameNetworkManager GameNetworkManager::get_Instance()
            * ldfld     string GameNetworkManager::currentSaveFileName
            * call      void ['Assembly-CSharp-firstpass']ES3::DeleteKey(string, string)
            *     AKA:
            * ES3.DeleteKey("UnlockedShipObjects", GameNetworkManager.Instance.currentSaveFileName);
            */
            codes.RemoveRange(rm, 4);
        }

        return codes.AsEnumerable();
    }
}

[HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.ResetShip))]
public class ResetShip {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        Plugin.log.LogWarning("Beginning transpilation of StartOfRound.ResetShip()");
        var codes = new List<CodeInstruction>(instructions);

        int prefabI1 = -1;
        CodeInstruction prefabJ1 = null;

        int prefabI2 = -1;
        CodeInstruction prefabJ2 = null;

        int p2 = -1;

        for (var i = 0; i < codes.Count; i++)
        {
            if (codes[i].opcode == OpCodes.Ldfld && codes[i].operand.ToString() == "System.Boolean spawnPrefab")
            {
                if (prefabI1 == -1)
                {
                    prefabI1 = i + 2;
                    prefabJ1 = codes[i + 1].Clone();
                    prefabJ1.opcode = OpCodes.Brfalse_S;
                    Plugin.log.LogInfo($"Found ship object prefab opcode at {i} (1)");
                }
                else if (prefabI2 == -1)
                {
                    prefabI2 = i + 2;
                    prefabJ2 = codes[i + 1].Clone();
                    prefabJ2.opcode = OpCodes.Brfalse_S;
                    Plugin.log.LogInfo($"Found ship object prefab opcode at {i} (1)");
                }
            }
            if (codes[i].opcode == OpCodes.Call && codes[i].operand.ToString() == "Void SwitchSuitForPlayer(GameNetcodeStuff.PlayerControllerB, Int32, Boolean)")
            {
                p2 = i;
                Plugin.log.LogInfo($"Found suit reset opcode at {i}");
            }
            if (codes[i].opcode == OpCodes.Stfld && codes[i].operand.ToString() == "System.Boolean disableObject")
            {
                // array[j].parentObject.disableObject = false; -> ... = true;
                codes[i - 1].opcode = OpCodes.Ldc_I4_1;
            }
        }

        if (p2 >= 0)
        {
            codes.RemoveRange(p2 - 6, 7);
        }

        if (prefabI2 >= 0)
        {
            codes.InsertRange(prefabI2, new CodeInstruction[] {
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Plugin), nameof(Plugin.shouldDefault))),
                prefabJ2
            });
        }

        if (prefabI1 >= 0)
        {
            codes.InsertRange(prefabI1, new CodeInstruction[]
            {
                new CodeInstruction(OpCodes.Ldloc_3),
                new CodeInstruction(OpCodes.Ldstr, "StartOfRound1"),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Plugin), nameof(Plugin.shouldReset))),
                prefabJ1
            });
        }

        return codes.AsEnumerable();
    }
}

[HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.SyncShipUnlockablesServerRpc))]
public class InSanityCheck
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        Plugin.log.LogWarning("Beginning transpilation of StartOfRound.SyncShipUnlockablesServerRpc()");
        var codes = new List<CodeInstruction>(instructions);

        for (int i = 0; i < codes.Count; i++)
        {
            if (codes[i].opcode == OpCodes.Ldstr && codes[i].operand.ToString() == "Server: placeableObject #{0}: {1}")
            {
                codes.InsertRange(i - 1, new CodeInstruction[] 
                {
                    new CodeInstruction(OpCodes.Ldloc_S, 5),
                    new CodeInstruction(OpCodes.Ldloc_S, 10),
                    new CodeInstruction(OpCodes.Ldelem_Ref),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(InSanityCheck), nameof(WhatIsGoingOn))) 
                }
                );
                Plugin.log.LogWarning("This crash is not evading me");
                break;
            }
        }

        return codes.AsEnumerable();
    }

    public static void WhatIsGoingOn(PlaceableShipObject pso)
    {
        Plugin.log.LogWarning("Seeing what the fuck is going wrong here with syncing unlockables, prepare for spam");
        Plugin.log.LogInfo($"ship object is not null? {pso != null}");
        Plugin.log.LogInfo($"ship object transform is not null? {pso.transform != null}");
        int id = pso.unlockableID;
        Plugin.log.LogInfo($"ship object unlockable id {id}");
        Plugin.log.LogInfo($"unlockables has id? {id < StartOfRound.Instance.unlockablesList.unlockables.Count}");
        UnlockableItem item = StartOfRound.Instance.unlockablesList.unlockables[id];
        Plugin.log.LogInfo($"unlockable is not null? {item != null}");
        Plugin.log.LogInfo($"unlockable position {item.placedPosition}");
        Plugin.log.LogInfo($"unlockable rotation {item.placedPosition}");
        Plugin.log.LogInfo($"unlockable in storage? {item.inStorage}");
    }
}