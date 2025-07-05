using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using REPOLib.Modules;
using UnityEngine;
using static HarmonyLib.AccessTools;

namespace SLRUpgradePack.UpgradeManagers;

public class ValuableDensityUpgrade : UpgradeBase<float> {
    public List<AnimationCurve> TotalMaxAmountCurves { get; set; }
    public List<AnimationCurve> TotalMaxValueCurves { get; set; }
    public List<AnimationCurve> TinyCurves { get; set; }
    public List<AnimationCurve> SmallCurves { get; set; }
    public List<AnimationCurve> MediumCurves { get;  set; }
    public List<AnimationCurve> BigCurves { get; set; }
    public List<AnimationCurve> WideCurves { get; set; }
    public List<AnimationCurve> TallCurves { get; set; }
    public List<AnimationCurve> VeryTallCurves { get; set; }

    public ValuableDensityUpgrade(bool enabled, float upgradeAmount, bool exponential, float exponentialAmount,
                                 ConfigFile config, AssetBundle assetBundle, float priceMultiplier) :
        base("Valuable Density", "assets/repo/mods/resources/items/items/item upgrade valuable density.asset", enabled,
             upgradeAmount, exponential, exponentialAmount, config, assetBundle, priceMultiplier, true, 2000, 100000) {
    }

    public override float Calculate(float value, PlayerAvatar player, int level) =>
        DefaultCalculateFloatIncrease(this, "ValuableDensity", value, player, level);
}

[HarmonyPatch(typeof(LevelGenerator), "Start")]
public class LevelGeneratorPatch {
    private static bool initialized = false;
    private delegate float difficultyDelegate();
    
    private static void Prefix(LevelGenerator __instance) {
        var valuableDensityUpgrade = SLRUpgradePack.ValuableDensityUpgradeInstance;
        if (SemiFunc.IsMasterClientOrSingleplayer() && valuableDensityUpgrade.UpgradeEnabled.Value) {
            SLRUpgradePack.Logger.LogInfo("Valuable Density Upgrade runs HERE");

            if (!initialized) {
                valuableDensityUpgrade.TinyCurves = GetNumberedFieldRefs<ValuableDirector, AnimationCurve>(ValuableDirector.instance, "tinyMaxAmountCurve", 10).Select(re => re.Invoke(ValuableDirector.instance)).ToList();
                valuableDensityUpgrade.SmallCurves = GetNumberedFieldRefs<ValuableDirector, AnimationCurve>(ValuableDirector.instance, "smallMaxAmountCurve", 10).Select(re => re.Invoke(ValuableDirector.instance)).ToList();
                valuableDensityUpgrade.MediumCurves = GetNumberedFieldRefs<ValuableDirector, AnimationCurve>(ValuableDirector.instance, "mediumMaxAmountCurve", 10).Select(re => re.Invoke(ValuableDirector.instance)).ToList();
                valuableDensityUpgrade.BigCurves = GetNumberedFieldRefs<ValuableDirector, AnimationCurve>(ValuableDirector.instance, "bigMaxAmountCurve", 10).Select(re => re.Invoke(ValuableDirector.instance)).ToList();
                valuableDensityUpgrade.WideCurves = GetNumberedFieldRefs<ValuableDirector, AnimationCurve>(ValuableDirector.instance, "wideMaxAmountCurve", 10).Select(re => re.Invoke(ValuableDirector.instance)).ToList();
                valuableDensityUpgrade.TallCurves = GetNumberedFieldRefs<ValuableDirector, AnimationCurve>(ValuableDirector.instance, "tallMaxAmountCurve", 10).Select(re => re.Invoke(ValuableDirector.instance)).ToList();
                valuableDensityUpgrade.VeryTallCurves = GetNumberedFieldRefs<ValuableDirector, AnimationCurve>(ValuableDirector.instance, "veryTallMaxAmountCurve", 10).Select(re => re.Invoke(ValuableDirector.instance)).ToList();
                valuableDensityUpgrade.TotalMaxAmountCurves = GetNumberedFieldRefs<ValuableDirector, AnimationCurve>(ValuableDirector.instance, "totalMaxAmountCurve", 10).Select(re => re.Invoke(ValuableDirector.instance)).ToList();;
                valuableDensityUpgrade.TotalMaxValueCurves = GetNumberedFieldRefs<ValuableDirector, AnimationCurve>(ValuableDirector.instance, "totalMaxValueCurve", 10).Select(re => re.Invoke(ValuableDirector.instance)).ToList();;
                initialized = true;
            }

            var numberedMethodDelegates = GetStaticNumberedMethodDelegates<difficultyDelegate>(typeof(SemiFunc), "RunGetDifficultyMultiplier", [], 10);
            
            foreach (var log in valuableDensityUpgrade.TotalMaxAmountCurves.Zip(numberedMethodDelegates, 
                                                                                (curve, difficulty) => $"Probably applying valuable density upgrade to {curve.Evaluate(difficulty())}")) {
                SLRUpgradePack.Logger.LogInfo(log);
            }

            var totalMaxAmountTraverse = Traverse.Create(ValuableDirector.instance).Field("totalMaxAmount");
            
            if(valuableDensityUpgrade.UpgradeRegister != null && valuableDensityUpgrade.UpgradeRegister.PlayerDictionary != null)
            foreach (var pair in valuableDensityUpgrade.UpgradeRegister.PlayerDictionary) {
                if (valuableDensityUpgrade.TotalMaxAmountCurves.Count != 0) {
                    SLRUpgradePack.Logger.LogInfo("Replacing Max Amount curve");
                    foreach (var element in GetNumberedFieldRefs<ValuableDirector, AnimationCurve>(ValuableDirector.instance, "totalMaxAmountCurve", 10)
                                .Zip(numberedMethodDelegates, Tuple.Create)
                                           .Select((value, index) => Tuple.Create(value, index))
                                           .Where(value => value.Item1.Item1 != null && value.Item1.Item2 != null)) {
                        element.Item1.Item1.Invoke(ValuableDirector.instance) = ReplaceCurve(valuableDensityUpgrade.TotalMaxAmountCurves[element.Item2], value => valuableDensityUpgrade.Calculate(value, SemiFunc.PlayerGetFromSteamID(pair.Key), pair.Value));
                    }
                } else {
                    SLRUpgradePack.Logger.LogInfo("Setting Max Amount");
                    totalMaxAmountTraverse.SetValue((int)Math.Ceiling(valuableDensityUpgrade.Calculate(totalMaxAmountTraverse.GetValue<int>(), SemiFunc.PlayerGetFromSteamID(pair.Key), pair.Value)));
                }

                if(valuableDensityUpgrade.TotalMaxValueCurves.Count != 0) {
                    SLRUpgradePack.Logger.LogInfo("Replacing Max Value curve");
                    foreach (var element in GetNumberedFieldRefs<ValuableDirector, AnimationCurve>(ValuableDirector.instance, "totalMaxValueCurve", 10)
                                           .Zip(numberedMethodDelegates, Tuple.Create)
                                           .Select((value, index) => Tuple.Create(value, index))
                                           .Where(value => value.Item1.Item1 != null && value.Item1.Item2 != null)) {
                        element.Item1.Item1.Invoke(ValuableDirector.instance) = ReplaceCurve(valuableDensityUpgrade.TotalMaxValueCurves[element.Item2], value => valuableDensityUpgrade.Calculate(value, SemiFunc.PlayerGetFromSteamID(pair.Key), pair.Value));
                    }
                }
                SLRUpgradePack.Logger.LogInfo("Replacing Tiny Max Amount curve");
                foreach (var element in GetNumberedFieldRefs<ValuableDirector, AnimationCurve>(ValuableDirector.instance, "tinyMaxAmountCurve", 10)
                                       .Zip(numberedMethodDelegates, Tuple.Create)
                                       .Select((value, index) => Tuple.Create(value, index))
                                       .Where(value => value.Item1.Item1 != null && value.Item1.Item2 != null)) {
                    element.Item1.Item1.Invoke(ValuableDirector.instance) = ReplaceCurve(valuableDensityUpgrade.TinyCurves[element.Item2], value => valuableDensityUpgrade.Calculate(value, SemiFunc.PlayerGetFromSteamID(pair.Key), pair.Value));
                }
                SLRUpgradePack.Logger.LogInfo("Replacing Small Max Amount curve");
                foreach (var element in GetNumberedFieldRefs<ValuableDirector, AnimationCurve>(ValuableDirector.instance, "smallMaxAmountCurve", 10)
                                       .Zip(numberedMethodDelegates, Tuple.Create)
                                       .Select((value, index) => Tuple.Create(value, index))
                                       .Where(value => value.Item1.Item1 != null && value.Item1.Item2 != null)) {
                    element.Item1.Item1.Invoke(ValuableDirector.instance) = ReplaceCurve(valuableDensityUpgrade.SmallCurves[element.Item2], value => valuableDensityUpgrade.Calculate(value, SemiFunc.PlayerGetFromSteamID(pair.Key), pair.Value));
                }
                SLRUpgradePack.Logger.LogInfo("Replacing Medium Max Amount curve");
                foreach (var element in GetNumberedFieldRefs<ValuableDirector, AnimationCurve>(ValuableDirector.instance, "mediumMaxAmountCurve", 10)
                                       .Zip(numberedMethodDelegates, Tuple.Create)
                                       .Select((value, index) => Tuple.Create(value, index))
                                       .Where(value => value.Item1.Item1 != null && value.Item1.Item2 != null)) {
                    element.Item1.Item1.Invoke(ValuableDirector.instance) = ReplaceCurve(valuableDensityUpgrade.MediumCurves[element.Item2], value => valuableDensityUpgrade.Calculate(value, SemiFunc.PlayerGetFromSteamID(pair.Key), pair.Value));
                }
                SLRUpgradePack.Logger.LogInfo("Replacing Big Max Amount curve");
                foreach (var element in GetNumberedFieldRefs<ValuableDirector, AnimationCurve>(ValuableDirector.instance, "bigMaxAmountCurve", 10)
                                       .Zip(numberedMethodDelegates, Tuple.Create)
                                       .Select((value, index) => Tuple.Create(value, index))
                                       .Where(value => value.Item1.Item1 != null && value.Item1.Item2 != null)) {
                    element.Item1.Item1.Invoke(ValuableDirector.instance) = ReplaceCurve(valuableDensityUpgrade.BigCurves[element.Item2], value => valuableDensityUpgrade.Calculate(value, SemiFunc.PlayerGetFromSteamID(pair.Key), pair.Value));
                }
                SLRUpgradePack.Logger.LogInfo("Replacing Wide Max Amount curve");
                foreach (var element in GetNumberedFieldRefs<ValuableDirector, AnimationCurve>(ValuableDirector.instance, "wideMaxAmountCurve", 10)
                                       .Zip(numberedMethodDelegates, Tuple.Create)
                                       .Select((value, index) => Tuple.Create(value, index))
                                       .Where(value => value.Item1.Item1 != null && value.Item1.Item2 != null)) {
                    element.Item1.Item1.Invoke(ValuableDirector.instance) = ReplaceCurve(valuableDensityUpgrade.WideCurves[element.Item2], value => valuableDensityUpgrade.Calculate(value, SemiFunc.PlayerGetFromSteamID(pair.Key), pair.Value));
                }
                SLRUpgradePack.Logger.LogInfo("Replacing Tall Max Amount curve");
                foreach (var element in GetNumberedFieldRefs<ValuableDirector, AnimationCurve>(ValuableDirector.instance, "tallMaxAmountCurve", 10)
                                       .Zip(numberedMethodDelegates, Tuple.Create)
                                       .Select((value, index) => Tuple.Create(value, index))
                                       .Where(value => value.Item1.Item1 != null && value.Item1.Item2 != null)) {
                    element.Item1.Item1.Invoke(ValuableDirector.instance) = ReplaceCurve(valuableDensityUpgrade.TallCurves[element.Item2], value => valuableDensityUpgrade.Calculate(value, SemiFunc.PlayerGetFromSteamID(pair.Key), pair.Value));
                }
                SLRUpgradePack.Logger.LogInfo("Replacing Very Tall Max Amount curve");
                foreach (var element in GetNumberedFieldRefs<ValuableDirector, AnimationCurve>(ValuableDirector.instance, "veryTallMaxAmountCurve", 10)
                                       .Zip(numberedMethodDelegates, Tuple.Create)
                                       .Select((value, index) => Tuple.Create(value, index))
                                       .Where(value => value.Item1.Item1 != null && value.Item1.Item2 != null)) {
                    element.Item1.Item1.Invoke(ValuableDirector.instance) = ReplaceCurve(valuableDensityUpgrade.VeryTallCurves[element.Item2], value => valuableDensityUpgrade.Calculate(value, SemiFunc.PlayerGetFromSteamID(pair.Key), pair.Value));
                }
            }
            
            SLRUpgradePack.Logger.LogDebug($"Total max items: {totalMaxAmountTraverse.GetValue<int>()}");
        }
    }

    private static AnimationCurve ReplaceCurve(AnimationCurve target, Func<float, float> calculate) {
        var newCurve = new AnimationCurve();
        
        newCurve.CopyFrom(target);
        newCurve.ClearKeys();
        
        foreach (var key in target.GetKeys()) {
            var newKey = key with {value = calculate.Invoke(key.value)};
            SLRUpgradePack.Logger.LogDebug($"Increased step at {newKey.time} from {key.value} to {newKey.value}");
            newCurve.AddKey(newKey);
        }

        return newCurve;
    }
    
    private static List<FieldRef<S, T>> GetNumberedFieldRefs<S, T>(S source, string expectedBaseName, int checkMax, int checkMin = 0) {
        var fields = new List<FieldRef<S, T>>();

        if (Traverse.Create(source).Field(expectedBaseName).FieldExists()) {
            fields.Add(FieldRefAccess<S, T>(expectedBaseName));
        }

        for (var i = checkMin; i < checkMax; i++) {
            if (Traverse.Create(source).Field(expectedBaseName + i).FieldExists()) {
                fields.Add(FieldRefAccess<S, T>(expectedBaseName + i));
            }
        }
        
        return fields;
    }

    private static List<T> GetNumberedMethodDelegates<S, T>(S source, string expectedBaseName, Type[] parameters, int checkMax, int checkMin = 0) where T : Delegate {
        var methods = new List<T>();

        if (Traverse.Create(source).Method(expectedBaseName, parameters).MethodExists()) {
            methods.Add(MethodDelegate<T>(Method(typeof(S), expectedBaseName, parameters), source, true));
        }
        
        for (var i = checkMin; i < checkMax; i++) {
            if (Traverse.Create(source).Method(expectedBaseName + i, parameters).MethodExists()) {
                methods.Add(MethodDelegate<T>(Method(typeof(S), expectedBaseName + i, parameters), source, true));
            }
        }
        
        return methods;
    }

    private static List<T> GetStaticNumberedMethodDelegates<T>(Type source, string expectedBaseName, Type[] parameters, int checkMax, int checkMin = 0) where T : Delegate {
        var methods = new List<T>();
        
        if (Traverse.Create(source).Method(expectedBaseName, parameters).MethodExists()) {
            methods.Add(MethodDelegate<T>(Method(source, expectedBaseName, parameters), source, true));
        }
        
        for (var i = checkMin; i < checkMax; i++) {
            if (Traverse.Create(source).Method(expectedBaseName + i, parameters).MethodExists()) {
                methods.Add(MethodDelegate<T>(Method(source, expectedBaseName + i, parameters), source, true));
            }
        }
        
        return methods;
    }
}