using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using CustomColors;
using HarmonyLib;
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

    public MapPlayerTrackerUpgrade(bool enabled, ConfigFile config, AssetBundle assetBundle, float priceMultiplier, bool arrowIcon, Color trackerColor, int minPrice, int maxPrice, bool playerColor) :
        base("Map Player Tracker", "assets/repo/mods/resources/items/items/item upgrade map player tracker.asset", enabled, 1, false, 1, config, assetBundle, priceMultiplier, false, minPrice, maxPrice, true, true) {
        ArrowIcon = config.Bind("Map Player Tracker Upgrade", "Arrow Icon", arrowIcon, "Whether the icon should appear as an arrow showing direction instead of a dot.");
        PlayerColor = config.Bind("Map Player Tracker Upgrade", "Player Color", playerColor, "Whether the icon should be colored as the player.");
        TrackerColor = config.Bind("Map Player Tracker Upgrade", "Color", trackerColor, "The color of the icon.");

        this.assetBundle = assetBundle;
    }

    public override int Calculate(int value, PlayerAvatar player, int level) {
        throw new NotImplementedException();
    }

    internal void AddPlayerToMap(PlayerAvatar playerAvatar) {
        if (UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) == 0)
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

    internal void RemovePlayerFromMap(PlayerAvatar playerAvatar) {
        if (UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) == 0)
            return;
        GameObject visuals = GetVisualsFromComponent(playerAvatar);
        if (visuals == null || removeFromMap.Contains(visuals))
            return;
        if (addToMap.Any(x => x.Item1 == visuals))
            addToMap.RemoveAll(x => x.Item1 == visuals);
        removeFromMap.Add(visuals);
    }

    internal void UpdateTracker() {
        if (SemiFunc.PlayerAvatarLocal() != null && UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) > 0) {
            for (int i = addToMap.Count - 1; i >= 0; i--) {
                (GameObject gameObject, Color color) = addToMap[i];
                addToMap.RemoveAt(i);
                MapCustom mapCustom = gameObject.GetComponent<MapCustom>();
                if (mapCustom != null)
                    continue;
                mapCustom = gameObject.AddComponent<MapCustom>();
                mapCustom.color = color;
                mapCustom.sprite = ArrowIcon.Value ? IntegrationResource.moreBundle.LoadAsset<Sprite>("Map Tracker") : SemiFunc.PlayerAvatarLocal().playerDeathHead.mapCustom.sprite;
            }

            for (int i = removeFromMap.Count - 1; i >= 0; i--) {
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

    internal override void InitUpgrade(PlayerAvatar player, int level) {
        base.InitUpgrade(player, level);

        if (playerTrackerComponent != null) Object.Destroy(playerTrackerComponent);
        playerTrackerComponent = new GameObject().AddComponent<PlayerTrackerComponent>();
    }
}

[HarmonyPatch(typeof(PlayerAvatar))]
internal class PlayerAvatarPatch {
    [HarmonyPatch("LateStart")]
    [HarmonyPostfix]
    static void LateStart(PlayerAvatar __instance) {
        var mapPlayerTrackerUpgrade = SLRUpgradePack.MapPlayerTrackerUpgradeInstance;
        if (mapPlayerTrackerUpgrade.UpgradeEnabled.Value == false || mapPlayerTrackerUpgrade.UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) == 0)
            return;
        mapPlayerTrackerUpgrade.AddPlayerToMap(__instance);
    }

    [HarmonyPatch("ReviveRPC")]
    [HarmonyPostfix]
    static void ReviveRPC(PlayerAvatar __instance) {
        var mapPlayerTrackerUpgrade = SLRUpgradePack.MapPlayerTrackerUpgradeInstance;
        if (mapPlayerTrackerUpgrade.UpgradeEnabled.Value == false || mapPlayerTrackerUpgrade.UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) == 0)
            return;
        mapPlayerTrackerUpgrade.AddPlayerToMap(__instance);
    }

    [HarmonyPatch("PlayerDeathRPC")]
    [HarmonyPostfix]
    static void PlayerDeathRPC(PlayerAvatar __instance) {
        var mapPlayerTrackerUpgrade = SLRUpgradePack.MapPlayerTrackerUpgradeInstance;
        if (mapPlayerTrackerUpgrade.UpgradeEnabled.Value == false || mapPlayerTrackerUpgrade.UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) == 0)
            return;
        mapPlayerTrackerUpgrade.RemovePlayerFromMap(__instance);
    }

    [HarmonyPatch("SetColorRPC")]
    [HarmonyPostfix]
    static void SetColorRPC(PlayerAvatar __instance) {
        var mapPlayerTrackerUpgrade = SLRUpgradePack.MapPlayerTrackerUpgradeInstance;
        if (mapPlayerTrackerUpgrade.UpgradeEnabled.Value == false || mapPlayerTrackerUpgrade.UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) == 0)
            return;
        if (mapPlayerTrackerUpgrade.PlayerColor.Value) {
            mapPlayerTrackerUpgrade.RemovePlayerFromMap(__instance);
            mapPlayerTrackerUpgrade.AddPlayerToMap(__instance);
        }
    }

    [HarmonyPatch(typeof(CustomColorsMod.ModdedColorPlayerAvatar))]
    internal class ModdedColorPlayerAvatarPatch {
        public static bool Prepare() {
            return Chainloader.PluginInfos.ContainsKey("x753.CustomColors");
        }

        [HarmonyPatch("ModdedSetColorRPC")]
        static void Postfix(CustomColorsMod.ModdedColorPlayerAvatar __instance) {
            var mapPlayerTrackerUpgrade = SLRUpgradePack.MapPlayerTrackerUpgradeInstance;
            if (mapPlayerTrackerUpgrade.UpgradeEnabled.Value == false || mapPlayerTrackerUpgrade.UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) == 0)
                return;
            PlayerAvatar playerAvatar = __instance.avatar;
            if (mapPlayerTrackerUpgrade.UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) > 0 && mapPlayerTrackerUpgrade.PlayerColor.Value) {
                mapPlayerTrackerUpgrade.RemovePlayerFromMap(playerAvatar);
                mapPlayerTrackerUpgrade.AddPlayerToMap(playerAvatar);
            }
        }
    }
}