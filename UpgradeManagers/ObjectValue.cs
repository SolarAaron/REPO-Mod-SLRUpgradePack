using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;
using static HarmonyLib.AccessTools;
using Object = UnityEngine.Object;

namespace SLRUpgradePack.UpgradeManagers;

public class ObjectValueUpgrade : UpgradeBase<float> {
    public ConfigEntry<bool> UpgradeScalesSurplus { get; protected set; }

    public ObjectValueUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount, ConfigFile config, AssetBundle assetBundle, float priceMultiplier) :
        base("Object Value", "assets/repo/mods/resources/items/items/item upgrade value.asset", enabled, upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, true, 2000, 100000, true, false) {
        UpgradeScalesSurplus = config.Bind("Object Value Upgrade", "Scale Surplus Bag", false,
                                           "Should the Object Value Upgrade scale the extraction surplus bag?");
    }

    public override float Calculate(float value, PlayerAvatar player, int level) => DefaultCalculateFloatIncrease(this, "ObjectValue", value, player, level);
}

[HarmonyPatch(typeof(ValuableObject), "DollarValueSetLogic")]
public class ValuableObjectValuePatch {
    internal static readonly FieldRef<ValuableObject, int> FixedValueRef = FieldRefAccess<ValuableObject, int>("dollarValueOverride");
    internal static readonly FieldRef<ValuableObject, float> DollarValueCurrentRef = FieldRefAccess<ValuableObject, float>("dollarValueCurrent");
    internal static readonly Queue<ValuableObject> DollarValueQueue = new();

    [HarmonyPriority(Priority.First)]
    internal static void Postfix(ValuableObject __instance) {
        var objectValueUpgrade = SLRUpgradePack.ObjectValueUpgradeInstance;

        if (__instance == null || SemiFunc.RunIsLobby() || SemiFunc.RunIsShop() || SemiFunc.RunIsArena()) return;

        if (objectValueUpgrade.UpgradeEnabled.Value) {
            if (LevelGenerator.Instance.State <= LevelGenerator.LevelState.Valuable) {
                if (!DollarValueQueue.Contains(__instance))
                    DollarValueQueue.Enqueue(__instance);
                return;
            }
        }

        Action(__instance);
    }

    internal static void Action(ValuableObject instance) {
        var objectValueUpgrade = SLRUpgradePack.ObjectValueUpgradeInstance;

        if (SemiFunc.IsMasterClientOrSingleplayer() && objectValueUpgrade.UpgradeEnabled.Value) {
            if (!objectValueUpgrade.UpgradeScalesSurplus.Value && instance.name.StartsWithIgnoreCaseFast("surplus")) return;

            SLRUpgradePack.Logger.LogDebug($"{instance.name} Original value: {instance.valuePreset.valueMin} - {instance.valuePreset.valueMax} ({DollarValueCurrentRef.Invoke(instance)} / {FixedValueRef.Invoke(instance)})");
            var customValue = Object.Instantiate(instance.valuePreset);
            var finalValue = DollarValueCurrentRef.Invoke(instance);

            foreach (var pair in objectValueUpgrade.UpgradeRegister.PlayerDictionary) {
                customValue.valueMin =
                    objectValueUpgrade.Calculate(instance.valuePreset.valueMin, SemiFunc.PlayerGetFromSteamID(pair.Key), pair.Value);
                customValue.valueMax =
                    objectValueUpgrade.Calculate(instance.valuePreset.valueMax, SemiFunc.PlayerGetFromSteamID(pair.Key), pair.Value);

                if (FixedValueRef.Invoke(instance) != 0) {
                    FixedValueRef.Invoke(instance) =
                        (int)Math.Ceiling(objectValueUpgrade.Calculate(FixedValueRef.Invoke(instance), SemiFunc.PlayerGetFromSteamID(pair.Key), pair.Value));
                }

                finalValue = objectValueUpgrade.Calculate(finalValue, SemiFunc.PlayerGetFromSteamID(pair.Key), pair.Value);
            }

            instance.valuePreset = customValue;
            DollarValueCurrentRef.Invoke(instance) = finalValue;
            SLRUpgradePack.Logger.LogDebug($"After calculation with levels {string.Join(",", objectValueUpgrade.UpgradeRegister.PlayerDictionary
                                                                                                               .Where(kvp => SemiFunc.PlayerAvatarGetFromSteamID(kvp.Key) != null)
                                                                                                               .Select(kvp => (SemiFunc.PlayerGetName(SemiFunc.PlayerAvatarGetFromSteamID(kvp.Key)), kvp.Value)))}: {instance.valuePreset.valueMin} - {instance.valuePreset.valueMax} ({DollarValueCurrentRef.Invoke(instance)} / {FixedValueRef.Invoke(instance)})");
        }
    }
}

[HarmonyPatch(typeof(LevelGenerator), "Start")]
public class LevelGeneratorObjectValueStartPatch {
    internal static void Postfix(ValuableDirector __instance) {
        if (SemiFunc.RunIsLevel()) {
            while (ValuableObjectValuePatch.DollarValueQueue.Count > 0) {
                var valuable = ValuableObjectValuePatch.DollarValueQueue.Dequeue();
                if (valuable)
                    ValuableObjectValuePatch.Action(valuable);
            }
        }
    }
}

public class SpawnedValuableValuePatch<TSVT> where TSVT : MonoBehaviour {
    protected static void DoValuableStuff(TSVT spawnedValuable) {
        if (spawnedValuable.TryGetComponent<ValuableObject>(out var valuableObject) && SLRUpgradePack.ObjectValueUpgradeInstance.UpgradeEnabled.Value) {
            SLRUpgradePack.Logger.LogDebug($"Valuable spawned with: {valuableObject.valuePreset.valueMin} {valuableObject.valuePreset.valueMax} {ValuableObjectValuePatch.FixedValueRef.Invoke(valuableObject)}");
            ValuableObjectValuePatch.Action(valuableObject);
        }
    }
}

[HarmonyPatch(typeof(SurplusValuable), "Start")]
public class SurplusValuableValuePatch : SpawnedValuableValuePatch<SurplusValuable> {
    internal static void Prefix(SurplusValuable __instance) {
        if (SLRUpgradePack.ObjectValueUpgradeInstance.UpgradeScalesSurplus.Value && SLRUpgradePack.ObjectValueUpgradeInstance.UpgradeEnabled.Value)
            DoValuableStuff(__instance);
    }
}

[HarmonyPatch(typeof(EnemyValuable), "Start")]
public class EnemyValuableValuePatch : SpawnedValuableValuePatch<EnemyValuable> {
    internal static void Prefix(EnemyValuable __instance) {
        DoValuableStuff(__instance);
    }
}