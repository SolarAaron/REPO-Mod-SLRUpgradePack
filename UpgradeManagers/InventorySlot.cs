using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using KeybindLib.Classes;
using Photon.Pun;
using TMPro;
using UnityEngine;
using static HarmonyLib.AccessTools;
using Object = UnityEngine.Object;

namespace SLRUpgradePack.UpgradeManagers;

public class InventorySlotUpgrade : UpgradeBase<int> {
    internal static readonly Dictionary<string, Dictionary<int, int>> serverMonitoredInventoryItems = new();
    public Keybind ItemSlot4 { get; set; }
    public Keybind ItemSlot5 { get; set; }
    public Keybind ItemSlot6 { get; set; }
    public Keybind ItemSlot7 { get; set; }
    public Keybind ItemSlot8 { get; set; }
    public Keybind ItemSlot9 { get; set; }
    internal string BoundPlayer { get; set; }

    public InventorySlotUpgrade(bool enabled, int upgradeAmount, ConfigFile config, AssetBundle assetBundle, float priceMultiplier) :
        base("Inventory Slot", "assets/repo/mods/resources/items/items/item upgrade inventory slot lib.asset", enabled, upgradeAmount, false, 0, config, assetBundle, priceMultiplier, false, false, 6) {
        MethodInfo RegisterMethod = Method(typeof(Keybinds), "Register");
        BepInPlugin plugin = typeof(SLRUpgradePack).GetCustomAttribute<BepInPlugin>();
        ItemSlot4 = (Keybind) RegisterMethod.Invoke(null, [plugin, "Item Slots", "Item Slot 4", "<keyboard>/z", null, false]);
        ItemSlot5 = (Keybind) RegisterMethod.Invoke(null, [plugin, "Item Slots", "Item Slot 5", "<keyboard>/x", null, false]);
        ItemSlot6 = (Keybind) RegisterMethod.Invoke(null, [plugin, "Item Slots", "Item Slot 6", "<keyboard>/c", null, false]);
        ItemSlot7 = (Keybind) RegisterMethod.Invoke(null, [plugin, "Item Slots", "Item Slot 7", "<keyboard>/n", null, false]);
        ItemSlot8 = (Keybind) RegisterMethod.Invoke(null, [plugin, "Item Slots", "Item Slot 8", "<keyboard>/m", null, false]);
        ItemSlot9 = (Keybind) RegisterMethod.Invoke(null, [plugin, "Item Slots", "Item Slot 9", "<keyboard>/,", null, false]);
    }

    public override int Calculate(int value, PlayerAvatar player, int level) {
        return value + level;
    }

    internal override void InitUpgrade(PlayerAvatar player, int level) {
        base.InitUpgrade(player, level);
        if (player == SemiFunc.PlayerAvatarLocal()) {
            BoundPlayer = SemiFunc.PlayerGetSteamID(player);
        }
    }
}

[HarmonyPatch(typeof(InventoryUI), "Start")]
public class InventoryUIStartPatch {
    private static FieldRef<InventoryUI, List<GameObject>> allChildren = FieldRefAccess<InventoryUI, List<GameObject>>("allChildren");

    private static void Postfix(InventoryUI __instance) {
        var inventorySlotUpgrade = SLRUpgradePack.InventorySlotUpgradeInstance;
        if (inventorySlotUpgrade.UpgradeEnabled.Value && inventorySlotUpgrade.BoundPlayer != null) {
            var slots = inventorySlotUpgrade.Calculate(3, SemiFunc.PlayerAvatarLocal(), inventorySlotUpgrade.UpgradeRegister.GetLevel(inventorySlotUpgrade.BoundPlayer));

            if (slots > 3) {
                SLRUpgradePack.Logger.LogInfo($"Redrawing for {slots} slots");
                float num = -(slots * 40) / 2f + 20f;
                Transform child = __instance.transform.GetChild(0);
                for (int j = 0; j < slots; j++) {
                    if (j < 3) {
                        __instance.transform.Find($"Inventory Spot {j + 1}").localPosition = new Vector2(num + j * 40f, -175.3f);
                        continue;
                    }
                    Transform val = Object.Instantiate(child, child.parent);
                    val.name = $"Inventory Spot {j + 1}";
                    InventorySpot component = val.GetComponent<InventorySpot>();
                    component.inventorySpotIndex = j;
                    TextMeshProUGUI component2 = val.Find("Numbers").GetComponent<TextMeshProUGUI>();
                    string text2 = (component.noItem.text = (j + 1).ToString());
                    component2.text = text2;
                    val.localPosition = new Vector2(num + j * 40f, -175.3f);
                    allChildren.Invoke(__instance).Add(val.gameObject);
                }
            }
        }
    }
}

[HarmonyPatch(typeof(StatsManager), "PlayerInventoryUpdate")]
public class StatsManagerUpdatePatch {
    private static void Postfix(StatsManager __instance, string _steamID, string itemName, int spot, bool sync) {
        if (!SemiFunc.IsMasterClientOrSingleplayer() || spot < 3) {
            return;
        }
        Dictionary<int, int> value;
        bool flag = InventorySlotUpgrade.serverMonitoredInventoryItems.TryGetValue(_steamID, out value);
        if (string.IsNullOrEmpty(itemName)) {
            if (flag) {
                value.Remove(spot);
                if (value.Count == 0) {
                    InventorySlotUpgrade.serverMonitoredInventoryItems.Remove(_steamID);
                }
            }
        } else {
            if (!flag) {
                InventorySlotUpgrade.serverMonitoredInventoryItems.Add(_steamID, value = new Dictionary<int, int>());
            }
            value[spot] = itemName.GetHashCode();
        }
    }
}

[HarmonyPatch(typeof(MainMenuOpen), "Start")]
public class MainMenuOpenStartPatch {
    private static void Postfix(MainMenuOpen __instance) {
        InventorySlotUpgrade.serverMonitoredInventoryItems.Clear();
    }
}

[HarmonyPatch(typeof(Inventory), "Start")]
public class InventoryStartPatch {
    private static FieldRef<Inventory, List<InventorySpot>> inventorySpots = FieldRefAccess<Inventory, List<InventorySpot>>("inventorySpots");

    private static void Postfix(Inventory __instance) {
        var inventorySlotUpgrade = SLRUpgradePack.InventorySlotUpgradeInstance;
        if (inventorySlotUpgrade.UpgradeEnabled.Value && inventorySlotUpgrade.BoundPlayer != null) {
            var extraSlots = inventorySlotUpgrade.Calculate(0, SemiFunc.PlayerAvatarLocal(), inventorySlotUpgrade.UpgradeRegister.GetLevel(inventorySlotUpgrade.BoundPlayer));

            SLRUpgradePack.Logger.LogInfo($"Player has {extraSlots} extra slots");
            for (int index = 0; index < extraSlots; ++index)
                inventorySpots.Invoke(__instance).Add(null);
        }
    }
}

[HarmonyPatch(typeof(InventorySpot), "Update")]
public class InventorySpotUpdatePatch {
    private static readonly MethodInfo HandleInputMethod = Method(typeof(InventorySpot), "HandleInput");

    private static void Prefix(InventorySpot __instance) {
        var inventorySlotUpgrade = SLRUpgradePack.InventorySlotUpgradeInstance;
        if (inventorySlotUpgrade.UpgradeEnabled.Value && inventorySlotUpgrade.BoundPlayer != null) {
            var slotCount = inventorySlotUpgrade.Calculate(3, SemiFunc.PlayerAvatarLocal(), inventorySlotUpgrade.UpgradeRegister.GetLevel(inventorySlotUpgrade.BoundPlayer));
            List<Keybind> slotInputs = [null, null, null, inventorySlotUpgrade.ItemSlot4, inventorySlotUpgrade.ItemSlot5, inventorySlotUpgrade.ItemSlot6, inventorySlotUpgrade.ItemSlot7, inventorySlotUpgrade.ItemSlot8, inventorySlotUpgrade.ItemSlot9];
            if (__instance.inventorySpotIndex > 2 && InputManager.instance.KeyDown(slotInputs.GetRange(0, slotCount)[__instance.inventorySpotIndex].inputKey)) {
                HandleInputMethod.Invoke(__instance, null);
            }
        }
    }
}

[HarmonyPatch(typeof(PunManager), "SetItemNameLOGIC")]
public class PunManagerSetItemNameLOGICPatch {
    private static bool Prefix(PunManager __instance, string name, int photonViewID, ItemAttributes _itemAttributes, StatsManager ___statsManager) {
        if (photonViewID == -1 && SemiFunc.IsMultiplayer())
            return true;
        ItemAttributes itemAttributes = _itemAttributes;
        if (SemiFunc.IsMultiplayer())
            itemAttributes = PhotonView.Find(photonViewID).GetComponent<ItemAttributes>();
        if ((Object) _itemAttributes == null && !SemiFunc.IsMultiplayer())
            return true;
        var instanceNameRef = FieldRefAccess<ItemAttributes, string>("instanceName");
        instanceNameRef.Invoke(itemAttributes) = name;
        ItemBattery component1 = itemAttributes.GetComponent<ItemBattery>();
        if (component1)
            component1.SetBatteryLife(___statsManager.itemStatBattery[name]);
        ItemEquippable itemEquippable = itemAttributes.GetComponent<ItemEquippable>();
        if (!itemEquippable)
            return true;
        int hashCode = name.GetHashCode();

        if (!itemEquippable) {
            return true;
        }

        foreach (PlayerAvatar item in SemiFunc.PlayerGetList()) {
            if (InventorySlotUpgrade.serverMonitoredInventoryItems.TryGetValue(SemiFunc.PlayerGetSteamID(item), out var value) && value.ContainsValue(hashCode)) {
                itemEquippable.RequestEquip(value.First(element => element.Value == hashCode).Key, SemiFunc.IsMultiplayer() ? item.photonView.ViewID : -1);
                return false;
            }
        }

        return true;
    }
}