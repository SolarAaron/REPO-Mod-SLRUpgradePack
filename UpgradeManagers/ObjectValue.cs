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
    public ConfigEntry<float> SurplusPercentScale { get; protected set; }

    public ObjectValueUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
        ConfigFile config, AssetBundle assetBundle, float priceMultiplier) :
        base("Object Value", "assets/repo/mods/resources/items/items/item upgrade value lib.asset", enabled,
            upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, true, true,
            ((int?)null)) {
        UpgradeScalesSurplus = config.Bind("Object Value Upgrade", "Scale Surplus Bag", false,
            "Should the Object Value Upgrade scale the extraction surplus bag?");
        SurplusPercentScale = config.Bind("Object Value Upgrade", "Surplus Scaled Percentage", 0.1f, "Percentage of the extraction surplus bag that is scaled by this upgrade");
    }

    public override float Calculate(float value, PlayerAvatar player, int level) =>
        DefaultCalculateFloatIncrease(this, "ObjectValue", value, player, level);
}

[HarmonyPatch(typeof(ValuableObject), "DollarValueSetLogic")]
public class ValuableObjectValuePatch {
    internal static readonly FieldRef<ValuableObject, int> FixedValueRef =
        FieldRefAccess<ValuableObject, int>("dollarValueOverride");

    internal static readonly FieldRef<ValuableObject, float> DollarValueCurrentRef =
        FieldRefAccess<ValuableObject, float>("dollarValueCurrent");

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
            if (!objectValueUpgrade.UpgradeScalesSurplus.Value &&
                instance.name.StartsWithIgnoreCaseFast("surplus")) return;

            SLRUpgradePack.Logger.LogDebug(
                $"{instance.name} Original value: {instance.valuePreset.valueMin} - {instance.valuePreset.valueMax} ({DollarValueCurrentRef.Invoke(instance)} / {FixedValueRef.Invoke(instance)})");
            var customValue = Object.Instantiate(instance.valuePreset);
            var finalValue = DollarValueCurrentRef.Invoke(instance);

            var totalLevels = objectValueUpgrade.UpgradeRegister.PlayerDictionary
                .Where(kvp => SemiFunc.PlayerAvatarGetFromSteamID(kvp.Key) != null)
                .Select(kvp => kvp.Value)
                .Sum();
            customValue.valueMin =
                objectValueUpgrade.Calculate(instance.valuePreset.valueMin, null, totalLevels);
            customValue.valueMax =
                objectValueUpgrade.Calculate(instance.valuePreset.valueMax, null, totalLevels);

            if (FixedValueRef.Invoke(instance) != 0) {
                FixedValueRef.Invoke(instance) =
                    (int)Math.Ceiling(objectValueUpgrade.Calculate(FixedValueRef.Invoke(instance), null, totalLevels));
            }

            finalValue = objectValueUpgrade.Calculate(finalValue, null, totalLevels);

            instance.valuePreset = customValue;
            DollarValueCurrentRef.Invoke(instance) = finalValue;
            SLRUpgradePack.Logger.LogDebug(
                $"After calculation with levels {string.Join(",", objectValueUpgrade.UpgradeRegister.PlayerDictionary
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
        if (spawnedValuable.TryGetComponent<ValuableObject>(out var valuableObject) &&
            SLRUpgradePack.ObjectValueUpgradeInstance.UpgradeEnabled.Value) {
            SLRUpgradePack.Logger.LogDebug(
                $"Valuable {spawnedValuable.name} spawned with: {valuableObject.valuePreset.valueMin} {valuableObject.valuePreset.valueMax} {ValuableObjectValuePatch.FixedValueRef.Invoke(valuableObject)}");
            ValuableObjectValuePatch.Action(valuableObject);
        }
    }
}

[HarmonyPatch(typeof(SurplusValuable), "Start")]
public class SurplusValuableValuePatch : SpawnedValuableValuePatch<SurplusValuable> {
    internal static void Prefix(SurplusValuable __instance) {
        var objectValueUpgradeInstance = SLRUpgradePack.ObjectValueUpgradeInstance;
        if (objectValueUpgradeInstance.UpgradeScalesSurplus.Value &&
            objectValueUpgradeInstance.UpgradeEnabled.Value && __instance.TryGetComponent<ValuableObject>(out var valuableObject)) {
            var customValue = Object.Instantiate(valuableObject.valuePreset); // has original scaled value
            var origMin = valuableObject.valuePreset.valueMin;
            var origMax = valuableObject.valuePreset.valueMax;
            var origFixed = ValuableObjectValuePatch.FixedValueRef.Invoke(valuableObject);
            var origCurrent = ValuableObjectValuePatch.DollarValueCurrentRef.Invoke(valuableObject);

            customValue.valueMin = origMin * objectValueUpgradeInstance.SurplusPercentScale.Value;
            customValue.valueMax = origMax * objectValueUpgradeInstance.SurplusPercentScale.Value;
            var origFixedScaled = ValuableObjectValuePatch.FixedValueRef.Invoke(valuableObject) = (int)(origFixed * objectValueUpgradeInstance.SurplusPercentScale.Value);
            var origCurrentScaled = ValuableObjectValuePatch.DollarValueCurrentRef.Invoke(valuableObject) = origCurrent * objectValueUpgradeInstance.SurplusPercentScale.Value;
            valuableObject.valuePreset = customValue;

            DoValuableStuff(__instance);

            valuableObject.valuePreset.valueMin += (origMin - customValue.valueMin);
            valuableObject.valuePreset.valueMax += (origMax - customValue.valueMax);
            ValuableObjectValuePatch.FixedValueRef.Invoke(valuableObject) += (origFixed - origFixedScaled);
            ValuableObjectValuePatch.DollarValueCurrentRef.Invoke(valuableObject) += (origCurrent - origCurrentScaled);

            SLRUpgradePack.Logger.LogInfo($"Surplus bag worth {origCurrent} scaling {origCurrentScaled} to a total of {ValuableObjectValuePatch.DollarValueCurrentRef.Invoke(valuableObject)}");
        }
    }
}

[HarmonyPatch(typeof(EnemyValuable), "Start")]
public class EnemyValuableValuePatch : SpawnedValuableValuePatch<EnemyValuable> {
    internal static void Prefix(EnemyValuable __instance) {
        DoValuableStuff(__instance);
    }
}
