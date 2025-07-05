using System.Collections.Generic;
using HarmonyLib;
using REPOLib.Modules;
using UnityEngine.Events;

namespace SLRUpgradePack.UpgradeManagers.MoreUpgrades;

[HarmonyPatch(typeof(ItemUpgrade), "Start")]
public class ItemUpgradePatch {
    private static void Postfix(ItemUpgrade __instance) {
        if (((List<string>) ["Item Upgrade Map Enemy Tracker", "Item Upgrade Map Player Tracker", "Item Upgrade Sprint Usage", "Item Upgrade Map Value Tracker"]).Contains(__instance.gameObject.GetComponent<ItemAttributes>().item.itemAssetName)) {
            var libItemUpgrade = __instance.gameObject.GetComponent<REPOLibItemUpgrade>();
            __instance.upgradeEvent = new UnityEvent();
            __instance.upgradeEvent.AddListener(libItemUpgrade.Upgrade);
        }
    }
}

[HarmonyPatch(typeof(ShopManager), "GetAllItemsFromStatsManager")]
public class ShopManagerSingleUsePatch {
    private static void Prefix(ShopManager __instance) {
        foreach (Item obj in StatsManager.instance.itemDictionary.Values) {
            if (((List<string>) ["Item Upgrade Map Enemy Tracker", "Item Upgrade Map Player Tracker", "Item Upgrade Map Value Tracker"]).Contains(obj.itemAssetName)) {
                obj.maxPurchaseAmount = GameDirector.instance.PlayerList.Count;
            }
        }
    }
}
