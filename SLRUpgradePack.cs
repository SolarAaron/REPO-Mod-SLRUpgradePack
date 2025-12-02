using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using REPOLib;
using SLRUpgradePack.UpgradeManagers;
using UnityEngine;
using Resources = SLRUpgradePack.Properties.Resources;

namespace SLRUpgradePack;

[BepInDependency(MyPluginInfo.PLUGIN_GUID)]
[BepInDependency("bulletbot.keybindlib")]
[BepInPlugin("SolarAaron.SLRUpgradePack", "SLRUpgradePack", "0.3.1")]
public class SLRUpgradePack : BaseUnityPlugin {
    internal static SLRUpgradePack Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger => Instance._logger;
    internal Harmony? Harmony { get; set; }
    private ManualLogSource _logger => base.Logger;
    internal static readonly Dictionary<string, int> LimitedUse = new();

    public static OverchargeUpgrade OverchargeUpgradeInstance { get; private set; }
    public static ArmorUpgrade ArmorUpgradeInstance { get; private set; }
    public static ObjectValueUpgrade ObjectValueUpgradeInstance { get; private set; }
    public static ObjectDurabilityUpgrade ObjectDurabilityUpgradeInstance { get; private set; }
    public static ValuableDensityUpgrade ValuableDensityUpgradeInstance { get; private set; }
    public static HeartOfGoldUpgrade HeartOfGoldUpgradeInstance { get; private set; }
    public static RegenerationUpgrade RegenerationUpgradeInstance { get; private set; }
    public static ExtraLifeUpgrade ExtraLifeUpgradeInstance { get; private set; }
    public static InventorySlotUpgrade InventorySlotUpgradeInstance { get; private set; }

    private void Awake() {
        Instance = this;

        // Prevent the plugin from being deleted
        this.gameObject.transform.parent = null;
        this.gameObject.hideFlags = HideFlags.HideAndDontSave;

        _logger.LogInfo("Configuring upgrade pack...");

        var assetBundle = AssetBundle.LoadFromMemory(Resources.slr_assets);
        assetBundle.name = "slr";

        foreach (var assetName in assetBundle.GetAllAssetNames()) {
            _logger.LogInfo($"Found asset: {assetName}");
        }

        if (PhysGrabberPatch.Prepare())
            OverchargeUpgradeInstance = new OverchargeUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 0.75f);
        ArmorUpgradeInstance = new ArmorUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 1.0f);
        ObjectValueUpgradeInstance = new ObjectValueUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 1.5f);
        ObjectDurabilityUpgradeInstance = new ObjectDurabilityUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 1.5f);
        ValuableDensityUpgradeInstance = new ValuableDensityUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 1.5f);
        HeartOfGoldUpgradeInstance = new HeartOfGoldUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 10f, 2.5f);
        RegenerationUpgradeInstance = new RegenerationUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, .1f, 1.5f);
        ExtraLifeUpgradeInstance = new ExtraLifeUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 5, 5f);
        InventorySlotUpgradeInstance = new InventorySlotUpgrade(true, 1, Config, assetBundle, 3f);

        Patch();
        Logger.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} has loaded!");
    }

    internal void Patch() {
        Harmony ??= new Harmony(Info.Metadata.GUID);
        Harmony.PatchAll();
    }

    internal void Unpatch() {
        Harmony?.UnpatchSelf();
    }

    private void Update() {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
        if (!LevelGenerator.Instance.Generated) return;

        var actions = new List<Action>();

        if (PhysGrabberPatch.Prepare() && OverchargeUpgradeInstance.UpgradeEnabled.Value)
            foreach (var pair in OverchargeUpgradeInstance.UpgradeRegister.PlayerDictionary)
                if (pair.Value < OverchargeUpgradeInstance.StartingAmount.Value)
                    actions.Add(() => OverchargeUpgradeInstance.UpgradeRegister.SetLevel(pair.Key, OverchargeUpgradeInstance.StartingAmount.Value));

        if (ArmorUpgradeInstance.UpgradeEnabled.Value)
            foreach (var pair in ArmorUpgradeInstance.UpgradeRegister.PlayerDictionary)
                if (pair.Value < ArmorUpgradeInstance.StartingAmount.Value)
                    actions.Add(() => ArmorUpgradeInstance.UpgradeRegister.SetLevel(pair.Key, ArmorUpgradeInstance.StartingAmount.Value));

        if (ObjectValueUpgradeInstance.UpgradeEnabled.Value)
            foreach (var pair in ObjectValueUpgradeInstance.UpgradeRegister.PlayerDictionary)
                if (pair.Value < ObjectValueUpgradeInstance.StartingAmount.Value)
                    actions.Add(() => ObjectValueUpgradeInstance.UpgradeRegister.SetLevel(pair.Key, ObjectValueUpgradeInstance.StartingAmount.Value));

        if (ObjectDurabilityUpgradeInstance.UpgradeEnabled.Value)
            foreach (var pair in ObjectDurabilityUpgradeInstance.UpgradeRegister.PlayerDictionary)
                if (pair.Value < ObjectDurabilityUpgradeInstance.StartingAmount.Value)
                    actions.Add(() => ObjectDurabilityUpgradeInstance.UpgradeRegister.SetLevel(pair.Key, ObjectDurabilityUpgradeInstance.StartingAmount.Value));

        if (ValuableDensityUpgradeInstance.UpgradeEnabled.Value)
            foreach (var pair in ValuableDensityUpgradeInstance.UpgradeRegister.PlayerDictionary)
                if (pair.Value < ValuableDensityUpgradeInstance.StartingAmount.Value)
                    actions.Add(() => ValuableDensityUpgradeInstance.UpgradeRegister.SetLevel(pair.Key, ValuableDensityUpgradeInstance.StartingAmount.Value));

        if (HeartOfGoldUpgradeInstance.UpgradeEnabled.Value)
            foreach (var pair in HeartOfGoldUpgradeInstance.UpgradeRegister.PlayerDictionary)
                if (pair.Value < HeartOfGoldUpgradeInstance.StartingAmount.Value)
                    actions.Add(() => HeartOfGoldUpgradeInstance.UpgradeRegister.SetLevel(pair.Key, HeartOfGoldUpgradeInstance.StartingAmount.Value));

        if (RegenerationUpgradeInstance.UpgradeEnabled.Value)
            foreach (var pair in RegenerationUpgradeInstance.UpgradeRegister.PlayerDictionary)
                if (pair.Value < RegenerationUpgradeInstance.StartingAmount.Value)
                    actions.Add(() => RegenerationUpgradeInstance.UpgradeRegister.SetLevel(pair.Key, RegenerationUpgradeInstance.StartingAmount.Value));

        if (ExtraLifeUpgradeInstance.UpgradeEnabled.Value)
            foreach (var pair in ExtraLifeUpgradeInstance.UpgradeRegister.PlayerDictionary)
                if (pair.Value < ExtraLifeUpgradeInstance.StartingAmount.Value)
                    actions.Add(() => ExtraLifeUpgradeInstance.UpgradeRegister.SetLevel(pair.Key, ExtraLifeUpgradeInstance.StartingAmount.Value));

        if (InventorySlotUpgradeInstance.UpgradeEnabled.Value)
            foreach (var pair in InventorySlotUpgradeInstance.UpgradeRegister.PlayerDictionary)
                if (pair.Value < InventorySlotUpgradeInstance.StartingAmount.Value)
                    actions.Add(() => InventorySlotUpgradeInstance.UpgradeRegister.SetLevel(pair.Key, InventorySlotUpgradeInstance.StartingAmount.Value));

        actions.ForEach(action => action.Invoke());
    }
}