using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SLRUpgradePack.UpgradeManagers.MoreUpgrades;

public class EnemyTrackerComponent : MonoBehaviour {
    private void Update() {
        SLRUpgradePack.MapEnemyTrackerUpgradeInstance.UpdateTracker();
    }
}

public class MapEnemyTrackerUpgrade : UpgradeBase<int> {
    public ConfigEntry<bool> ArrowIcon { get; set; }
    public ConfigEntry<Color> TrackerColor { get; set; }
    public ConfigEntry<string> ExcludeEnemies { get; set; }
    private List<(GameObject, Color)> addToMap = [];
    private List<GameObject> removeFromMap = [];
    private AssetBundle assetBundle;
    private EnemyTrackerComponent enemyTrackerComponent;

    public MapEnemyTrackerUpgrade(bool enabled, ConfigFile config, AssetBundle assetBundle, float priceMultiplier, bool arrowIcon, Color trackerColor,
                                  string excludeEnemies, int minPrice, int maxPrice) :
        base("Map Enemy Tracker", "Map Enemy Tracker", enabled, 1, false, 1, config, assetBundle, priceMultiplier, false, minPrice, maxPrice, false, true) {
        ArrowIcon = config.Bind("Map Enemy Tracker Upgrade", "Arrow Icon", arrowIcon, "Whether the icon should appear as an arrow showing direction instead of a dot.");
        TrackerColor = config.Bind("Map Enemy Tracker Upgrade", "Color", trackerColor, "The color of the icon.");
        ExcludeEnemies = config.Bind("Map Enemy Tracker Upgrade", "Exclude Enemies", excludeEnemies, "Exclude specific enemies from displaying their icon by listing their names." +
                                                                                                     "\nExample: 'Gnome, Clown', seperated by commas.");

        this.assetBundle = assetBundle;
    }

    public override int Calculate(int value, PlayerAvatar player, int level) {
        throw new NotImplementedException();
    }

    internal void AddEnemyToMap(Component component, string enemyName = null) {
        if (UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) == 0)
            return;

        if (component is EnemyParent enemyParent && enemyName == null)
            enemyName = enemyParent.enemyName;
        if (ExcludeEnemies.Value.Split(',').Select(x => x.Trim())
                          .Where(x => !string.IsNullOrEmpty(x)).Contains(enemyName))
            return;
        GameObject visuals = GetVisualsFromComponent(component);
        if (visuals == null || addToMap.Any(x => x.Item1 == visuals))
            return;
        if (removeFromMap.Contains(visuals))
            removeFromMap.Remove(visuals);
        addToMap.Add((visuals, TrackerColor.Value));
    }

    internal void RemoveEnemyFromMap(Component component, string enemyName = null) {
        if (UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) == 0)
            return;
        if (component is EnemyParent enemyParent && enemyName == null)
            enemyName = enemyParent.enemyName;
        if (ExcludeEnemies.Value.Split(',').Select(x => x.Trim())
                          .Where(x => !string.IsNullOrEmpty(x)).Contains(enemyName))
            return;
        GameObject visuals = GetVisualsFromComponent(component);
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
                mapCustom.sprite = ArrowIcon.Value ? assetBundle.LoadAsset<Sprite>("Map Tracker") : SemiFunc.PlayerAvatarLocal().playerDeathHead.mapCustom.sprite;
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

        if (enemyTrackerComponent != null) Object.Destroy(enemyTrackerComponent);
        enemyTrackerComponent = new GameObject().AddComponent<EnemyTrackerComponent>();
    }
}

[HarmonyPatch(typeof(EnemyParent))]
internal class EnemyParentPatch {
    [HarmonyPatch("SpawnRPC")]
    [HarmonyPostfix]
    static void SpawnRPC(EnemyParent __instance) {
        var mapEnemyTrackerUpgrade = SLRUpgradePack.MapEnemyTrackerUpgradeInstance;
        if (mapEnemyTrackerUpgrade.UpgradeEnabled.Value == false || mapEnemyTrackerUpgrade.UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) == 0)
            return;
        mapEnemyTrackerUpgrade.AddEnemyToMap(__instance);
    }

    [HarmonyPatch("DespawnRPC")]
    [HarmonyPostfix]
    static void DespawnRPC(EnemyParent __instance) {
        var mapEnemyTrackerUpgrade = SLRUpgradePack.MapEnemyTrackerUpgradeInstance;
        if (mapEnemyTrackerUpgrade.UpgradeEnabled.Value == false || mapEnemyTrackerUpgrade.UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) == 0)
            return;
        mapEnemyTrackerUpgrade.RemoveEnemyFromMap(__instance);
    }
}

[HarmonyPatch(typeof(EnemyHealth))]
internal class EnemyHealthPatch {
    [HarmonyPatch("DeathRPC")]
    [HarmonyPostfix]
    static void DeathRPC(EnemyHealth __instance, Enemy ___enemy) {
        var mapEnemyTrackerUpgrade = SLRUpgradePack.MapEnemyTrackerUpgradeInstance;
        if (mapEnemyTrackerUpgrade.UpgradeEnabled.Value == false || mapEnemyTrackerUpgrade.UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) == 0)
            return;
        mapEnemyTrackerUpgrade.RemoveEnemyFromMap((EnemyParent)AccessTools.Field(typeof(Enemy), "EnemyParent").GetValue(___enemy));
    }
}

[HarmonyPatch(typeof(EnemySlowMouth))]
internal class EnemySlowMouthPatch {
    [HarmonyPatch("UpdateStateRPC")]
    [HarmonyPostfix]
    static void UpdateStateRPC(EnemySlowMouth __instance, Enemy ___enemy) {
        var mapEnemyTrackerUpgrade = SLRUpgradePack.MapEnemyTrackerUpgradeInstance;
        var mapPlayerTrackerUpgrade = SLRUpgradePack.MapPlayerTrackerUpgradeInstance;
        if (mapEnemyTrackerUpgrade.UpgradeEnabled.Value == false || mapEnemyTrackerUpgrade.UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) == 0)
            return;
        PlayerAvatar playerTarget = (PlayerAvatar)AccessTools.Field(typeof(EnemySlowMouth), "playerTarget").GetValue(__instance);
        EnemyParent enemyParent = (EnemyParent)AccessTools.Field(typeof(Enemy), "EnemyParent").GetValue(___enemy);
        EnemySlowMouth.State state = __instance.currentState;
        if (state == EnemySlowMouth.State.Attached) {
            mapEnemyTrackerUpgrade.RemoveEnemyFromMap(enemyParent);
            if (playerTarget == SemiFunc.PlayerAvatarLocal())
                return;
            mapPlayerTrackerUpgrade.RemovePlayerFromMap(playerTarget);
            mapEnemyTrackerUpgrade.AddEnemyToMap(playerTarget, enemyParent.enemyName);
        } else if (state == EnemySlowMouth.State.Detach) {
            mapEnemyTrackerUpgrade.AddEnemyToMap(enemyParent);
            if (playerTarget == SemiFunc.PlayerAvatarLocal())
                return;
            mapPlayerTrackerUpgrade.AddPlayerToMap(playerTarget);
            mapEnemyTrackerUpgrade.RemoveEnemyFromMap(playerTarget, enemyParent.enemyName);
        }
    }
}

[HarmonyPatch(typeof(PlayerAvatar))]
internal class PlayerAvatarEnemyPatch {
    [HarmonyPatch("PlayerDeathRPC")]
    [HarmonyPostfix]
    static void PlayerDeathRPC(PlayerAvatar __instance) {
        var mapEnemyTrackerUpgrade = SLRUpgradePack.MapEnemyTrackerUpgradeInstance;
        if (mapEnemyTrackerUpgrade.UpgradeEnabled.Value == false || mapEnemyTrackerUpgrade.UpgradeRegister.GetLevel(SemiFunc.PlayerAvatarLocal()) == 0)
            return;
        mapEnemyTrackerUpgrade.RemoveEnemyFromMap(__instance);
    }
}