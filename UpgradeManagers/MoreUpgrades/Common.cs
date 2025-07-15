using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Resources = SLRUpgradePack.Properties.Resources;

namespace SLRUpgradePack.UpgradeManagers.MoreUpgrades;

internal class IntegrationResource {
    public static AssetBundle moreBundle = AssetBundle.LoadFromMemory(Resources.moreupgrades);
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