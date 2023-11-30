using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using LethalAssist;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;

namespace LethalAssist
{
    [BepInPlugin("beeisyou.PersistentPurchases", "Persistent Purchases", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource log = new ManualLogSource("Persistent Purchases");
        public static Harmony harmony = new Harmony("beeisyou.PersistentPurchases");
        private void Awake()
        {
            BepInEx.Logging.Logger.Sources.Add(log);

            harmony.PatchAll(typeof(Patches));
            harmony.PatchAll(typeof(ResetSavedGameValues));
            harmony.PatchAll(typeof(ResetShip));

            log.LogMessage("Plugin Persistent Purchases is loaded!");
        }

        public static bool shouldReset(int id, string debugType)
        {
            return false;
        }
    }
}

[HarmonyPatch]
public class Patches
{
    [HarmonyPrefix, HarmonyPatch(typeof(GameNetworkManager), nameof(GameNetworkManager.ResetUnlockablesListValues))]
    public static bool dontResetAnything()
    {
        /*if (StartOfRound.Instance != null)
        {
            UnityEngine.Debug.Log("CONDITIONALLY resetting unlockables list!");
            List<UnlockableItem> list = StartOfRound.Instance.unlockablesList.unlockables;
            for (int i = 0; i < list.Count; i++)
            {
                if (Plugin.shouldReset(i, "dontResetAnything")) // the only modified line lol
                {
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
        }*/
        return false; // skip original function
    }


    // [HarmonyPostfix, HarmonyPatch(typeof(GameNetworkManager), nameof(GameNetworkManager.ResetSavedGameValues))]
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
            if (array[i].parentObject == null)
            {
                Debug.Log("Error! No parentObject for placeable object: " + StartOfRound.Instance.unlockablesList.unlockables[array[i].unlockableID].unlockableName);
            }
            if (StartOfRound.Instance.unlockablesList.unlockables[array[i].unlockableID].spawnPrefab)
            {
                Collider[] componentsInChildren = array[i].parentObject.GetComponentsInChildren<Collider>();
                for (int j = 0; j < componentsInChildren.Length; j++)
                {
                    componentsInChildren[j].enabled = true;
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

        List<Tuple<int, CodeInstruction>> jumpTos = new List<Tuple<int, CodeInstruction>>();
        int p1 = -1;
        CodeInstruction jumpTo1 = null;

        int p2 = -1;

        for (var i = 0; i < codes.Count; i++)
        {
            if (codes[i].opcode == OpCodes.Ldfld && codes[i].operand.ToString() == "System.Boolean spawnPrefab")
            {
                CodeInstruction temp = codes[i + 1].Clone();
                temp.opcode = OpCodes.Br;
                jumpTos.Add(Tuple.Create(i + 2, temp));
                /*if (p1 == -1)
                {
                    p1 = i + 2;
                    jumpTo1 = codes[i + 1].Clone();
                    jumpTo1.opcode = OpCodes.Brfalse_S;
                    Plugin.log.LogInfo($"Found ship object prefab opcode at {i} (1)");
                }*/
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
        for (var i = jumpTos.Count - 1; i >= 0; i--)
        {
            codes.Insert(jumpTos[i].Item1, jumpTos[i].Item2);
        }
        if (p1 >= 0)
        {
            /*
             *     INSERTS:
             * ldloc.3                          // loads i onto stack
             * callvirt  Plugin.shouldReset     // inputs i to function, storing boolean output on stack
             * brfalse.s IL_0177                // skips to next iteration if shouldReset returned false
             * 
             * if (!this.unlockablesList.unlockables[i].alreadyUnlocked && this.unlockablesList.unlockables[i].spawnPrefab)
             *     ->
             * if (... && Plugins.shouldReset(i))
             */
            jumpTo1.opcode = OpCodes.Br;
            codes.InsertRange(p1, new CodeInstruction[] {
                // new CodeInstruction(OpCodes.Ldloc_3),
                // new CodeInstruction(OpCodes.Ldstr, "StartOfRound1"),
                // new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Plugin), nameof(Plugin.shouldReset))),
                jumpTo1
            });
        }

        for(var i = 0; i < codes.Count; i++)
        {
            Plugin.log.LogInfo(codes[i].ToString());
        }

        return codes.AsEnumerable();
    }
}