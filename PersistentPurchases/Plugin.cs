using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;

namespace PersistentPurchases
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static ConfigEntry<bool> placeUnlockables;
        public static ManualLogSource log = new ManualLogSource(PluginInfo.PLUGIN_NAME);
        public static Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);

        private void Awake()
        {
            BepInEx.Logging.Logger.Sources.Add(log);
            despair.Add(Config);

            // does not work :(
            // placeUnlockables = Config.Bind("General", "DefaultPlacement", true, "Reset objects to their default position, and put everything else in storage");

            harmony.PatchAll(typeof(Patches));
            harmony.PatchAll(typeof(RemoveUneccesaryAndAnnoyingReset));
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }

        public static string[] knownFurniture =
        {
            // vanilla
            "Cozy lights", "Television", "Cupboard", "File Cabinet", "Toilet", "Shower", "Light switch", "Record player", 
            "Table", "Bunkbeds", "Terminal", "Romantic table", "JackOLantern", "Welcome mat", "Goldfish", "Plushie pajama man",
            // lethal things
            "Small Rug", "Large Rug", "Fatalities Sign"
        };
        // unfathomably cursed and breaks several coding / geneva conventions
        public static List<ConfigFile> despair = new List<ConfigFile>();
        public static List<ConfigEntry<bool>> unlockableConfig = new List<ConfigEntry<bool>>();
        public static void setupConfig(List<UnlockableItem> unlockables)
        {
            log.LogInfo($"Registering {unlockables.Count} unlockables");
            for (int i = 0; i < unlockables.Count; i++)
            {
                log.LogDebug($"{unlockables[i].unlockableName}");
                // suit and furniture default config is keep
                unlockableConfig.Add(despair[0].Bind(
                    "Unlockables", unlockables[i].unlockableName, 
                    unlockables[i].unlockableType == 0 || knownFurniture.Contains(unlockables[i].unlockableName)
                ));
            }
        }
    }
}