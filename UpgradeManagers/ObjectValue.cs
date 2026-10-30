using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
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
    private sealed class ExtractionGoalValueData {
        internal float BaseValue;
        internal float UpgradeBonus;
        internal bool HasBaseValue;
        internal bool Excluded;
    }

    internal static readonly FieldRef<ValuableObject, int> FixedValueRef =
        FieldRefAccess<ValuableObject, int>("dollarValueOverride");

    internal static readonly FieldRef<ValuableObject, float> DollarValueCurrentRef =
        FieldRefAccess<ValuableObject, float>("dollarValueCurrent");
    private static readonly FieldRef<ValuableObject, bool> DollarValueSetRef =
        FieldRefAccess<ValuableObject, bool>("dollarValueSet");

    private static readonly ConditionalWeakTable<ValuableObject, ExtractionGoalValueData> ExtractionGoalValues = new();
    private static readonly FieldInfo DollarValueCurrentField =
        Field(typeof(ValuableObject), "dollarValueCurrent")!;
    private static readonly FieldInfo RoundDirectorHaulGoalMaxField =
        Field(typeof(RoundDirector), "haulGoalMax")!;
    private static readonly MethodInfo GetExtractionGoalValueMethod =
        Method(typeof(ValuableObjectValuePatch), nameof(GetExtractionGoalValue), [typeof(ValuableObject)])!;

    internal static readonly Queue<ValuableObject> DollarValueQueue = new();

    [HarmonyPriority(Priority.First)]
    internal static void Prefix(ValuableObject __instance) {
        if (__instance && !DollarValueSetRef.Invoke(__instance) &&
            ExtractionGoalValues.TryGetValue(__instance, out var goalValue)) {
            goalValue.HasBaseValue = false;
            goalValue.UpgradeBonus = 0f;
        }
    }

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
        if (!instance) return;

        EnsureBaseExtractionGoalValue(instance, DollarValueCurrentRef.Invoke(instance));

        var objectValueUpgrade = SLRUpgradePack.ObjectValueUpgradeInstance;

        if (SemiFunc.IsMasterClientOrSingleplayer() && objectValueUpgrade.UpgradeEnabled.Value) {
            if (!objectValueUpgrade.UpgradeScalesSurplus.Value &&
                instance.name.StartsWith("surplus", StringComparison.OrdinalIgnoreCase)) return;

            SLRUpgradePack.Logger.LogDebug(
                $"{instance.name} Original value: {instance.valuePreset.valueMin} - {instance.valuePreset.valueMax} ({DollarValueCurrentRef.Invoke(instance)} / {FixedValueRef.Invoke(instance)})");
            var customValue = Object.Instantiate(instance.valuePreset);
            var finalValue = DollarValueCurrentRef.Invoke(instance);

            var totalLevels = objectValueUpgrade.GetTotalActivePlayerLevels();
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
            UpdateExtractionGoalBonusFromCurrent(instance);
            SLRUpgradePack.Logger.LogDebug(
                $"After calculation with levels {string.Join(",", objectValueUpgrade.GetActivePlayerLevels()
                    .Select(entry => (SemiFunc.PlayerGetName(entry.Player), entry.Level)))}: {instance.valuePreset.valueMin} - {instance.valuePreset.valueMax} ({DollarValueCurrentRef.Invoke(instance)} / {FixedValueRef.Invoke(instance)})");
        }
    }

    internal static void EnsureBaseExtractionGoalValue(ValuableObject instance, float value) {
        if (!instance) return;

        var goalValue = ExtractionGoalValues.GetValue(instance, _ => new ExtractionGoalValueData());
        if (goalValue.HasBaseValue) return;

        goalValue.BaseValue = value;
        goalValue.HasBaseValue = true;
    }

    internal static void UpdateExtractionGoalBonusFromCurrent(ValuableObject instance) {
        if (!instance || !ExtractionGoalValues.TryGetValue(instance, out var goalValue) || !goalValue.HasBaseValue)
            return;

        goalValue.UpgradeBonus = DollarValueCurrentRef.Invoke(instance) - goalValue.BaseValue;
    }

    internal static void MarkExcludedFromExtractionGoal(ValuableObject instance) {
        if (!instance) return;

        ExtractionGoalValues.GetValue(instance, _ => new ExtractionGoalValueData()).Excluded = true;
    }

    internal static float GetExtractionGoalValue(ValuableObject instance) {
        if (!instance) return 0f;
        if (!ExtractionGoalValues.TryGetValue(instance, out var goalValue))
            return DollarValueCurrentRef.Invoke(instance);
        if (goalValue.Excluded) return 0f;

        var currentValue = DollarValueCurrentRef.Invoke(instance);
        // Goal sizing uses the pre-upgrade value; live haul and extraction always use dollarValueCurrent.
        return goalValue.HasBaseValue
            ? Mathf.Max(0f, currentValue - goalValue.UpgradeBonus)
            : currentValue;
    }

    internal static IEnumerable<CodeInstruction> ReplaceSpawnBudgetValueLoad(
        IEnumerable<CodeInstruction> instructions) {
        var replacements = 0;
        var patchedInstructions = new List<CodeInstruction>();

        foreach (var originalInstruction in instructions) {
            var instruction = new CodeInstruction(originalInstruction);
            if (instruction.LoadsField(DollarValueCurrentField)) {
                instruction.opcode = OpCodes.Call;
                instruction.operand = GetExtractionGoalValueMethod;
                replacements++;
            }

            patchedInstructions.Add(instruction);
        }

        if (replacements != 1)
            SLRUpgradePack.Logger.LogWarning(
                $"Expected one valuable spawn-budget value read in ValuableDirector.SpawnValuable, replaced {replacements}.");

        return patchedInstructions;
    }

    internal static IEnumerable<CodeInstruction> ReplaceExtractionGoalValueLoad(
        IEnumerable<CodeInstruction> instructions) {
        var replacements = 0;
        var patchedInstructions = instructions.Select(instruction => new CodeInstruction(instruction)).ToList();

        for (var i = 0; i < patchedInstructions.Count - 3; i++) {
            var instruction = patchedInstructions[i];
            if (!instruction.LoadsField(DollarValueCurrentField) ||
                patchedInstructions[i + 1].opcode != OpCodes.Conv_I4 ||
                patchedInstructions[i + 2].opcode != OpCodes.Add ||
                !patchedInstructions[i + 3].StoresField(RoundDirectorHaulGoalMaxField))
                continue;

            instruction.opcode = OpCodes.Call;
            instruction.operand = GetExtractionGoalValueMethod;
            replacements++;
        }

        if (replacements != 1)
            SLRUpgradePack.Logger.LogWarning(
                $"Expected one value read when building RoundDirector.haulGoalMax, replaced {replacements}.");

        return patchedInstructions;
    }
}

[HarmonyPatch]
public static class ValuableObjectExtractionGoalValuePatch {
    private static IEnumerable<MethodBase> TargetMethods() {
        var dollarValueSetMethod = Method(typeof(ValuableObject), "DollarValueSet");
        var stateMachineType = dollarValueSetMethod?.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType
            ?? typeof(ValuableObject).GetNestedTypes(BindingFlags.NonPublic)
                .FirstOrDefault(type => type.Name.StartsWith("<DollarValueSet>d__", StringComparison.Ordinal));
        var moveNextMethod = stateMachineType == null ? null : Method(stateMachineType, "MoveNext");
        if (moveNextMethod == null) {
            SLRUpgradePack.Logger.LogWarning(
                "Could not locate ValuableObject.DollarValueSet's iterator; extraction-goal value patch was skipped.");
            yield break;
        }

        yield return moveNextMethod;
    }

    // Ignore Object Value's bonus only when REPO accumulates the level's extraction goal.
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        ValuableObjectValuePatch.ReplaceExtractionGoalValueLoad(instructions);
}

[HarmonyPatch(typeof(ValuableDirector), "SpawnValuable")]
public static class ValuableDirectorSpawnBudgetGoalValuePatch {
    // Keep Object Value's bonus from prematurely consuming the valuable spawn budget.
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        ValuableObjectValuePatch.ReplaceSpawnBudgetValueLoad(instructions);
}

[HarmonyPatch(typeof(LevelGenerator), "Start")]
public class LevelGeneratorObjectValueStartPatch {
    internal static void Postfix() {
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

            ValuableObjectValuePatch.EnsureBaseExtractionGoalValue(valuableObject, origCurrent);

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
            ValuableObjectValuePatch.UpdateExtractionGoalBonusFromCurrent(valuableObject);

            SLRUpgradePack.Logger.LogInfo($"Surplus bag worth {origCurrent} scaling {origCurrentScaled} to a total of {ValuableObjectValuePatch.DollarValueCurrentRef.Invoke(valuableObject)}");
        }
    }
}

[HarmonyPatch(typeof(PhysGrabObjectImpactDetector))]
public static class ValuableObjectDynamicGoalValuePatch {
    private static readonly FieldRef<PhysGrabObjectImpactDetector, ValuableObject> ValuableObjectRef =
        FieldRefAccess<PhysGrabObjectImpactDetector, ValuableObject>("valuableObject");

    [HarmonyPatch("HealLogic")]
    [HarmonyPostfix]
    private static void HealLogicPostfix(PhysGrabObjectImpactDetector __instance) => RefreshGoalValue(__instance);

    [HarmonyPatch("ResetObject")]
    [HarmonyPostfix]
    private static void ResetObjectPostfix(PhysGrabObjectImpactDetector __instance) => RefreshGoalValue(__instance);

    private static void RefreshGoalValue(PhysGrabObjectImpactDetector instance) {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        var valuableObject = ValuableObjectRef.Invoke(instance);
        if (valuableObject)
            ValuableObjectValuePatch.UpdateExtractionGoalBonusFromCurrent(valuableObject);
    }
}

[HarmonyPatch(typeof(EnemyValuable), "Start")]
public class EnemyValuableValuePatch : SpawnedValuableValuePatch<EnemyValuable> {
    internal static void Prefix(EnemyValuable __instance) {
        DoValuableStuff(__instance);
    }
}
