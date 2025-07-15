using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using static HarmonyLib.AccessTools;
using Object = UnityEngine.Object;

namespace SLRUpgradePack.UpgradeManagers.MoreUpgrades;

public class MapValueTrackerComponent : MonoBehaviour {
    private FieldRef<ValuableObject, float>? _dollarValueCurrentRef = FieldRefAccess<ValuableObject, float>("dollarValueCurrent");

    private void Update() {
        if (SemiFunc.RunIsLobby() || SemiFunc.RunIsShop())
            return;
        var mapValueTrackerUpgrade = SLRUpgradePack.MapValueTrackerUpgradeInstance;
        if (MissionUI.instance != null && mapValueTrackerUpgrade.UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) != 0) {
            TextMeshProUGUI Text = (TextMeshProUGUI)Field(typeof(MissionUI), "Text").GetValue(MissionUI.instance);
            string messagePrev = (string)Field(typeof(MissionUI), "messagePrev").GetValue(MissionUI.instance);
            int count = mapValueTrackerUpgrade.currentValuables.Count;
            bool displayTotalValue = mapValueTrackerUpgrade.DisplayTotalValue.Value;
            int value = displayTotalValue ? mapValueTrackerUpgrade.currentValuables.Select(x => (int)_dollarValueCurrentRef.Invoke(x)).Sum() : 0;
            if (!Text.text.IsNullOrWhiteSpace() && (mapValueTrackerUpgrade.changed || mapValueTrackerUpgrade.previousCount != count || mapValueTrackerUpgrade.previousValue != value)) {
                SLRUpgradePack.Logger.LogInfo("Calculating map value");
                string text = Text.text;
                if (!mapValueTrackerUpgrade.changed && (mapValueTrackerUpgrade.previousCount != count || mapValueTrackerUpgrade.previousValue != value))
                    text = text.Substring(0, text.Length - mapValueTrackerUpgrade.textLength);
                string valuableText = $"\nValuables: <b>{count}</b>" +
                                      (displayTotalValue ? $" (<color=#558B2F>$</color><b>{SemiFunc.DollarGetString(value)}</b>)" : "");
                SLRUpgradePack.Logger.LogInfo($"{valuableText}");
                text += valuableText;
                mapValueTrackerUpgrade.previousCount = count;
                mapValueTrackerUpgrade.previousValue = value;
                mapValueTrackerUpgrade.textLength = valuableText.Length;
                Text.text = text;
                Field(typeof(MissionUI), "messagePrev").SetValue(MissionUI.instance, text);
                if (mapValueTrackerUpgrade.changed)
                    mapValueTrackerUpgrade.changed = false;
            }
        }
    }
}

public class MapValueTrackerUpgrade : UpgradeBase<int> {
    public ConfigEntry<bool> DisplayTotalValue { get; set; }
    internal List<ValuableObject> currentValuables = new();
    internal bool changed;
    internal int previousCount;
    internal int previousValue;
    internal int textLength;
    private MapValueTrackerComponent mapValueTrackerComponent;

    public MapValueTrackerUpgrade(bool enabled, ConfigFile config, AssetBundle assetBundle, float priceMultiplier, int minPrice, int maxPrice, bool displayTotal) :
        base("Map Value Tracker", "assets/repo/mods/resources/items/items/item upgrade map value tracker.asset", enabled, 1, false, 1, config, assetBundle, priceMultiplier, false, minPrice, maxPrice, false, true) {
        DisplayTotalValue = config.Bind("Map Value Tracker Upgrade", "Display Total Value", displayTotal, "Whether to display the total value next to the valuable counter.");
    }

    public override int Calculate(int value, PlayerAvatar player, int level) {
        throw new NotImplementedException();
    }

    internal override void InitUpgrade(PlayerAvatar player, int level) {
        base.InitUpgrade(player, level);
        changed = false;
        previousCount = 0;
        previousValue = 0;
        textLength = 0;

        if (mapValueTrackerComponent != null) Object.Destroy(mapValueTrackerComponent);
        mapValueTrackerComponent = new GameObject().AddComponent<MapValueTrackerComponent>();
    }
}

[HarmonyPatch(typeof(MissionUI), "MissionText")]
internal class MissionUIPatch {
    internal static void Prefix(MissionUI __instance, out string __state) {
        if (__instance != null)
            __state = Field(typeof(MissionUI), "messagePrev").GetValue(__instance) as string;
        else __state = null;
    }

    internal static void Postfix(MissionUI __instance, string __state) {
        string messagePrev = __instance == null ? null : (string)Field(typeof(MissionUI), "messagePrev").GetValue(__instance);
        if (__state != messagePrev) {
            SLRUpgradePack.MapValueTrackerUpgradeInstance.changed = true;
        }
    }
}

[HarmonyPatch(typeof(ValuableObject))]
internal class ValuableObjectTrackerPatch {
    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    static void Start(ValuableObject __instance) {
        var mapValueTrackerUpgrade = SLRUpgradePack.MapValueTrackerUpgradeInstance;
        if (mapValueTrackerUpgrade.UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) == 0)
            return;
        SLRUpgradePack.Logger.LogInfo($"Start tracking {__instance.name}");
        if (!mapValueTrackerUpgrade.currentValuables.Contains(__instance))
            mapValueTrackerUpgrade.currentValuables.Add(__instance);
    }
}

[HarmonyPatch(typeof(PhysGrabObject))]
internal class PhysGrabObjectTrackerPatch {
    [HarmonyPatch("OnDestroy")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    static void OnDestroy(PhysGrabObject __instance) {
        var mapValueTrackerUpgrade = SLRUpgradePack.MapValueTrackerUpgradeInstance;
        if (mapValueTrackerUpgrade.UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) == 0)
            return;
        ValuableObject valuableObject = __instance.gameObject.GetComponent<ValuableObject>();
        if (mapValueTrackerUpgrade.currentValuables.Contains(valuableObject))
            mapValueTrackerUpgrade.currentValuables.Remove(valuableObject);
    }
}