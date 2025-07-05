using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using REPOLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SLRUpgradePack.UpgradeManagers.MoreUpgrades;

public class PlayerTrackerComponent : MonoBehaviour {
    private void Update() {
        SLRUpgradePack.MapPlayerTrackerUpgradeInstance.UpdateTracker();
    }
}

public class MapPlayerTrackerUpgrade : UpgradeBase<int> {
    public ConfigEntry<bool> ArrowIcon { get; set; }
    public ConfigEntry<bool> PlayerColor { get; set; }
    public ConfigEntry<Color> TrackerColor { get; set; }
    private List<(GameObject, Color)> addToMap = [];
    private List<GameObject> removeFromMap = [];
    private AssetBundle assetBundle;
    private PlayerTrackerComponent playerTrackerComponent;

    public MapPlayerTrackerUpgrade(bool enabled, int upgradeAmount, bool exponential, int exponentialAmount, ConfigFile config, AssetBundle assetBundle, float priceMultiplier, bool configureAmount, bool arrowIcon, Color trackerColor, int minPrice, int maxPrice, bool playerColor) :
        base("Map Player Tracker", "Map Player Tracker", enabled, upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, configureAmount, minPrice, maxPrice) {
        ArrowIcon = config.Bind("Map Player Tracker", "Arrow Icon", arrowIcon, "Whether the icon should appear as an arrow showing direction instead of a dot.");
        PlayerColor = config.Bind("Map Player Tracker", "Player Color", playerColor, "Whether the icon should be colored as the player.");
        TrackerColor = config.Bind("Map Player Tracker", "Color", trackerColor, "The color of the icon.");

        this.assetBundle = assetBundle;
    }

    public override int Calculate(int value, PlayerAvatar player, int level) {
        throw new System.NotImplementedException();
    }

    internal void AddPlayerToMap(PlayerAvatar playerAvatar)
    {
        if (UpgradeLevel == 0)
            return;
        GameObject visuals = GetVisualsFromComponent(playerAvatar);
        if (visuals == null || addToMap.Any(x => x.Item1 == visuals))
            return;
        if (removeFromMap.Contains(visuals))
            removeFromMap.Remove(visuals);
        Color color = TrackerColor.Value;
        if (PlayerColor.Value)
            color = (Color)AccessTools.Field(typeof(PlayerAvatarVisuals), "color").GetValue(playerAvatar.playerAvatarVisuals);
        addToMap.Add((visuals, color));
    }

    internal void RemovePlayerFromMap(PlayerAvatar playerAvatar)
    {
        if (UpgradeLevel == 0)
            return;
        GameObject visuals = GetVisualsFromComponent(playerAvatar);
        if (visuals == null || removeFromMap.Contains(visuals))
            return;
        if (addToMap.Any(x => x.Item1 == visuals))
            addToMap.RemoveAll(x => x.Item1 == visuals);
        removeFromMap.Add(visuals);
    }
    
    internal void UpdateTracker()
    {
        if (SemiFunc.PlayerAvatarLocal() != null && UpgradeLevel > 0) {
            for (int i = addToMap.Count - 1; i >= 0; i--)
            {
                (GameObject gameObject, Color color) = addToMap[i];
                addToMap.RemoveAt(i);
                MapCustom mapCustom = gameObject.GetComponent<MapCustom>();
                if (mapCustom != null)
                    continue;
                mapCustom = gameObject.AddComponent<MapCustom>();
                mapCustom.color = color;
                mapCustom.sprite = ArrowIcon.Value ? assetBundle.LoadAsset<Sprite>("Map Tracker") :
                                       SemiFunc.PlayerAvatarLocal().playerDeathHead.mapCustom.sprite;
            }
            for (int i = removeFromMap.Count - 1; i >= 0; i--)
            {
                GameObject gameObject = removeFromMap[i];
                removeFromMap.RemoveAt(i);
                MapCustom mapCustom = gameObject.GetComponent<MapCustom>();
                if (mapCustom == null)
                    continue;
                Object.Destroy(mapCustom.mapCustomEntity.gameObject);
                Object.Destroy(mapCustom);
            }
        }
    }

    protected override void InitUpgrade(PlayerAvatar player, int level) {
        base.InitUpgrade(player, level);
        
        if(playerTrackerComponent != null) Object.Destroy(playerTrackerComponent);
        playerTrackerComponent = new GameObject().AddComponent<PlayerTrackerComponent>();
    }
}

[HarmonyPatch(typeof(PlayerAvatar))]
internal class PlayerAvatarPatch
{
    [HarmonyPatch("LateStart")]
    [HarmonyPostfix]
    static void LateStart(PlayerAvatar __instance)
    {
        var mapPlayerTrackerUpgrade = SLRUpgradePack.MapPlayerTrackerUpgradeInstance;
        if (mapPlayerTrackerUpgrade.UpgradeEnabled.Value == false || mapPlayerTrackerUpgrade.UpgradeLevel == 0)
            return;
        mapPlayerTrackerUpgrade.AddPlayerToMap(__instance);
    }

    [HarmonyPatch("ReviveRPC")]
    [HarmonyPostfix]
    static void ReviveRPC(PlayerAvatar __instance)
    {
        var mapPlayerTrackerUpgrade = SLRUpgradePack.MapPlayerTrackerUpgradeInstance;
        if (mapPlayerTrackerUpgrade.UpgradeEnabled.Value == false || mapPlayerTrackerUpgrade.UpgradeLevel == 0)
            return;
        mapPlayerTrackerUpgrade.AddPlayerToMap(__instance);
    }

    [HarmonyPatch("PlayerDeathRPC")]
    [HarmonyPostfix]
    static void PlayerDeathRPC(PlayerAvatar __instance)
    {
        var mapPlayerTrackerUpgrade = SLRUpgradePack.MapPlayerTrackerUpgradeInstance;
        if (mapPlayerTrackerUpgrade.UpgradeEnabled.Value == false || mapPlayerTrackerUpgrade.UpgradeLevel == 0)
            return;
        mapPlayerTrackerUpgrade.RemovePlayerFromMap(__instance);
    }

    [HarmonyPatch("SetColorRPC")]
    [HarmonyPostfix]
    static void SetColorRPC(PlayerAvatar __instance)
    {
        var mapPlayerTrackerUpgrade = SLRUpgradePack.MapPlayerTrackerUpgradeInstance;
        if (mapPlayerTrackerUpgrade.UpgradeEnabled.Value == false || mapPlayerTrackerUpgrade.UpgradeLevel == 0)
            return;
        if (mapPlayerTrackerUpgrade.PlayerColor.Value)
        {
            mapPlayerTrackerUpgrade.RemovePlayerFromMap(__instance);
            mapPlayerTrackerUpgrade.AddPlayerToMap(__instance);
        }
    }
}