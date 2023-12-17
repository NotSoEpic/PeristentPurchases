using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements.Collections;

namespace PersistentPurchases
{
    [HarmonyPatch]
    public class Patches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(Terminal), "Start")]
        [HarmonyPriority(-23)] // should be run after everything else registers their unlockables to the list
        public static void generateConfig()
        {
            Plugin.setupConfig(StartOfRound.Instance.unlockablesList.unlockables);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.ResetShip))]
        public static void storeUnlocked(StartOfRound __instance, out List<Tuple<int, Vector3, Vector3>> __state)
        {
            Plugin.log.LogInfo("Taking note of bought unlockables");
            __state = new List<Tuple<int, Vector3, Vector3>>();
            List<UnlockableItem> items = __instance.unlockablesList.unlockables;
            for (int i = 0; i < items.Count; i++)
            {
                Plugin.log.LogDebug($"{items[i].unlockableName} - unlocked({items[i].hasBeenUnlockedByPlayer}) - should persist({Plugin.unlockableConfig[i].Value})");
                if (items[i].hasBeenUnlockedByPlayer && Plugin.unlockableConfig[i].Value)
                {
                    __state.Add(Tuple.Create(i, items[i].placedPosition, items[i].placedRotation));
                }
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(StartOfRound), nameof(StartOfRound.ResetShip))]
        public static void loadUnlocked(StartOfRound __instance, List<Tuple<int, Vector3, Vector3>> __state)
        {
            Plugin.log.LogInfo("Rebuying unlockables");
            foreach (Tuple<int, Vector3, Vector3> t in __state) 
            {
                Plugin.log.LogDebug(__instance.unlockablesList.unlockables[t.Item1].unlockableName);

                var item = __instance.unlockablesList.unlockables[t.Item1];

                __instance.BuyShipUnlockableServerRpc(t.Item1, TimeOfDay.Instance.quotaVariables.startingCredits);
                if (__instance.SpawnedShipUnlockables.ContainsKey(t.Item1))
                {
                    NetworkObject networkObject = __instance.SpawnedShipUnlockables.Get(t.Item1).GetComponent<NetworkObject>();
                    if (networkObject != null)
                    {
                        // does not work :(
                        // ShipBuildModeManager.Instance.PlaceShipObjectServerRpc(t.Item2, t.Item3, networkObject, 0);
                        ShipBuildModeManager.Instance.StoreObjectServerRpc(networkObject, 0);
                    }
                    else
                    {
                        Plugin.log.LogWarning($"Failed to find NetworkObject for {item.unlockableName}");
                    }
                }
                else
                {
                    Plugin.log.LogWarning($"SpawnedShipUnlockables did not contain {item.unlockableName}");
                }
                /*item.hasBeenUnlockedByPlayer = true;
                if (item.unlockableType == 0)
                {
                    __instance.GetType().GetMethod("SpawnUnlockable", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(__instance, new object[] { i });
                }*/
            }
        }
    }

    // GameNetworkManager.ResetSavedGameValues() -> cancel calling this.ResetUnlockablesListValues();
    // ResetUnlockablesListValues is called in a really inconvenient place before this mod stores unlockables
    // it is also called later in the only sequence this function is called from, in a much nicer place for what i want to do
    [HarmonyPatch(typeof(GameNetworkManager), nameof(GameNetworkManager.ResetSavedGameValues))]
    public class RemoveUneccesaryAndAnnoyingReset()
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            Plugin.log.LogInfo("Beginning patching of GameNetworkManager.ResetSavedGameValues");
            var codes = new List<CodeInstruction>(instructions);
            var success = false;

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(typeof(GameNetworkManager).GetMethod("ResetUnlockablesListValues")))
                {
                    success = true;
                    codes[i - 1].opcode = OpCodes.Nop; // prevents loading now unused variable onto stack
                    codes[i].opcode = OpCodes.Nop;
                    codes[i].operand = null;
                    break;
                }
            }

            if (success)
            {
                Plugin.log.LogInfo("Patched GameNetworkManager.ResetSavedGameValues");
            }
            else
            {
                Plugin.log.LogError("Failed to patch GameNetworkManager.ResetSavedGameValues!");
            }

            return codes.AsEnumerable();
        }
    }
}
