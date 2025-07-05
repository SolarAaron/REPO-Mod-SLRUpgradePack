using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using REPOLib.Modules;
using SLRUpgradePack.UpgradeManagers;
using SLRUpgradePack.UpgradeManagers.MoreUpgrades;
using UnityEngine;

namespace SLRUpgradePack;

[BepInPlugin("SolarAaron.SLRUpgradePack", "SLRUpgradePack", "0.1.2")]
[BepInDependency(REPOLib.MyPluginInfo.PLUGIN_GUID)]
[BepInDependency("x753.CustomColors", BepInDependency.DependencyFlags.SoftDependency)]
public class SLRUpgradePack : BaseUnityPlugin
{
    internal static SLRUpgradePack Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger => Instance._logger;
    private ManualLogSource _logger => base.Logger;
    internal Harmony? Harmony { get; set; }
    
    public static OverchargeUpgrade OverchargeUpgradeInstance { get; private set; }
    public static ArmorUpgrade ArmorUpgradeInstance { get; private set; }
    public static ObjectValueUpgrade ObjectValueUpgradeInstance { get; private set; }
    public static ObjectDurabilityUpgrade ObjectDurabilityUpgradeInstance { get; private set; } 
    public static ValuableDensityUpgrade ValuableDensityUpgradeInstance { get; private set; } 
    public static HeartOfGoldUpgrade HeartOfGoldUpgradeInstance { get; private set; }
    public static RegenerationUpgrade RegenerationUpgradeInstance { get; private set; }
    public static ExtraLifeUpgrade ExtraLifeUpgradeInstance { get; private set; }
    public static MapEnemyTrackerUpgrade MapEnemyTrackerUpgradeInstance {  get; private set; }
    public static MapPlayerTrackerUpgrade MapPlayerTrackerUpgradeInstance { get; private set; }
    public static SprintUsageUpgrade SprintUsageUpgradeInstance { get; private set; }
    public static MapValueTrackerUpgrade MapValueTrackerUpgradeInstance { get; private set; }

    private void Awake()
    {
        Instance = this;

        // Prevent the plugin from being deleted
        this.gameObject.transform.parent = null;
        this.gameObject.hideFlags = HideFlags.HideAndDontSave;

        _logger.LogInfo("Configuring upgrade pack...");

        var assetBundle = AssetBundle.LoadFromMemory(Properties.Resources.slr_assets);
        assetBundle.name = "slr";
        var moreBundle = AssetBundle.LoadFromMemory(Properties.Resources.moreupgrades);
        moreBundle.name = "more";
        
        foreach (var assetName in assetBundle.GetAllAssetNames().Concat(moreBundle.GetAllAssetNames())) {
            _logger.LogInfo($"Found asset: {assetName}");
        }

        if(PhysGrabberPatch.Prepare())
            OverchargeUpgradeInstance = new OverchargeUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 0.75f);
        ArmorUpgradeInstance = new ArmorUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 1.0f);
        ObjectValueUpgradeInstance = new ObjectValueUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 1.5f);
        ObjectDurabilityUpgradeInstance = new ObjectDurabilityUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 1.5f);
        ValuableDensityUpgradeInstance = new ValuableDensityUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 1.5f);
        HeartOfGoldUpgradeInstance = new HeartOfGoldUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 10f, 2.5f);
        RegenerationUpgradeInstance = new RegenerationUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, .1f, 1.5f);
        ExtraLifeUpgradeInstance = new ExtraLifeUpgrade(true, 0.1f, false, 1.1f, Config, assetBundle, 5, 5f);

        MapEnemyTrackerUpgradeInstance = new MapEnemyTrackerUpgrade(true, Config, moreBundle, 4f, true, Color.red, "", 2000, 3000);
        MapPlayerTrackerUpgradeInstance = new MapPlayerTrackerUpgrade(true, Config, moreBundle, 4f, true, Color.blue, 2000, 3000, false);
        SprintUsageUpgradeInstance = new SprintUsageUpgrade(true, Config, moreBundle, 0.5f, 2000, 3000);
        MapValueTrackerUpgradeInstance = new MapValueTrackerUpgrade(true, Config, moreBundle, 3.5f, 2000, 3000, true);
        
        Patch();
        Logger.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} has loaded!");
    }

    internal void Patch()
    {
        Harmony ??= new Harmony(Info.Metadata.GUID);
        Harmony.PatchAll();
    }

    internal void Unpatch()
    {
        Harmony?.UnpatchSelf();
    }
}