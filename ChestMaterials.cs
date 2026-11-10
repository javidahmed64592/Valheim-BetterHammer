using System.Collections.Generic;

using BepInEx.Configuration;

using HarmonyLib;

using TMPro;

using UnityEngine;

namespace NoHammerBuildMenu;

/// <summary>
/// Lets building draw materials from nearby chests after the player's own inventory runs out.
/// </summary>
internal static class ChestMaterials
{
    private static ConfigEntry<bool> _enabled;
    private static ConfigEntry<float> _radius;
    private static ConfigEntry<float> _groundRange;

    private static readonly List<Container> Containers = [];
    private static readonly List<Container> NearbyCache = [];
    private static readonly AccessTools.FieldRef<Container, ZNetView> NviewRef =
        AccessTools.FieldRefAccess<Container, ZNetView>("m_nview");

    private static readonly List<ItemDrop> Drops = [];
    private static readonly AccessTools.FieldRef<ItemDrop, ZNetView> DropNviewRef =
        AccessTools.FieldRefAccess<ItemDrop, ZNetView>("m_nview");

    private static int _nearbyFrame = -1;
    private static Vector3 _nearbyPos;

    /// <summary>
    /// >0 while the player's inventory counts should include chests (requirement checks / display).
    /// </summary>
    private static int _countDepth;
    /// <summary>
    /// True while the resources of the placed piece are being consumed.
    /// </summary>
    private static bool _consuming;
    /// <summary>
    /// True while the build panel's total is counted: it includes ground items owned by other players.
    /// </summary>
    private static bool _countAllGround;

    public static void BindConfig(ConfigFile config)
    {
        // Chests
        _enabled = config.Bind("Chests", "Use Nearby Chests", true,
            new ConfigDescription(
                "When building, take any materials you are missing from your inventory out of nearby chests. (Synced with server)",
                null, Synced()));
        _radius = config.Bind("Chests", "Radius", 30f,
            new ConfigDescription("Distance in metres from the player within which chests are used for building materials. (Synced with server)",
                new AcceptableValueRange<float>(1f, 128f), Synced()));

        // Ground items
        _groundRange = config.Bind("Ground Items", "Radius", 30f,
            new ConfigDescription("Distance in metres from the player within which items lying on the ground are used for building materials. Set to 0 to disable. (Synced with server)",
                new AcceptableValueRange<float>(0f, 64f), Synced()));
    }

    /// <summary>Settings that use the server's value when the server has the mod.</summary>
    private static ConfigurationManagerAttributes Synced()
    {
        return new ConfigurationManagerAttributes { IsAdminOnly = true };
    }

    private static bool Active(Player player)
    {
        return _enabled != null && player != null && player == Player.m_localPlayer
        && (_enabled.Value || _groundRange.Value > 0f);
    }

    /// <summary>
    /// Dropped items within range that match the requested item.
    /// </summary>
    private static List<ItemDrop> GetGroundMatches(Player player, string name, int quality, bool matchWorldLevel)
    {
        List<ItemDrop> result = [];
        if (_groundRange.Value <= 0f)
        {
            return result;
        }

        Vector3 pos = player.transform.position;
        float maxSqr = _groundRange.Value * _groundRange.Value;
        _ = Drops.RemoveAll(d => d == null);
        foreach (ItemDrop drop in Drops)
        {
            ItemDrop.ItemData data = drop.m_itemData;
            ZNetView nview = DropNviewRef(drop);
            if (data == null || nview == null || !nview.IsValid()
                || drop.IsPiece()
                || data.m_shared.m_name != name
                || (quality >= 0 && quality != data.m_quality)
                || (matchWorldLevel && data.m_worldLevel < Game.m_worldLevel)
                || (drop.transform.position - pos).sqrMagnitude > maxSqr)
            {
                continue;
            }

            result.Add(drop);
        }

        result.Sort((a, b) =>
            (a.transform.position - pos).sqrMagnitude.CompareTo((b.transform.position - pos).sqrMagnitude));
        return result;
    }

    /// <summary>
    /// Whether this client may use a ground item now. Like vanilla pickup, only the item's owner may remove it,
    /// so an item owned by someone else is asked for (vanilla's RequestOwn) and becomes usable once it is
    /// handed over, usually within a moment. This way nobody can pick up an item that was also used to build.
    /// </summary>
    private static bool OwnedOrRequest(ItemDrop drop)
    {
        if (DropNviewRef(drop).IsOwner())
        {
            return true;
        }

        drop.RequestOwn();
        return false;
    }

    private static int TakeFromGround(Player player, string name, int amount, int quality, bool matchWorldLevel)
    {
        foreach (ItemDrop drop in GetGroundMatches(player, name, quality, matchWorldLevel))
        {
            if (amount <= 0)
            {
                break;
            }

            ZNetView nview = DropNviewRef(drop);
            if (!OwnedOrRequest(drop))
            {
                continue;
            }

            int take = Mathf.Min(drop.m_itemData.m_stack, amount);
            amount -= take;
            if (take >= drop.m_itemData.m_stack)
            {
                nview.Destroy();
            }
            else
            {
                drop.SetStack(drop.m_itemData.m_stack - take);
            }
        }
        return amount;
    }

    /// <summary>
    /// Chests within range that this player may take from, nearest first.
    /// </summary>
    private static List<Container> GetNearby(Player player)
    {
        Vector3 pos = player.transform.position;
        if (_nearbyFrame == Time.frameCount && _nearbyPos == pos)
        {
            return NearbyCache;
        }

        _nearbyFrame = Time.frameCount;
        _nearbyPos = pos;
        NearbyCache.Clear();
        if (!_enabled.Value)
        {
            return NearbyCache;
        }

        float maxSqr = _radius.Value * _radius.Value;
        _ = Containers.RemoveAll(c => c == null);
        foreach (Container container in Containers)
        {
            if ((container.transform.position - pos).sqrMagnitude <= maxSqr && CanTakeFrom(container))
            {
                NearbyCache.Add(container);
            }
        }

        NearbyCache.Sort((a, b) =>
            (a.transform.position - pos).sqrMagnitude.CompareTo((b.transform.position - pos).sqrMagnitude));
        return NearbyCache;
    }

    /// <summary>
    /// Only player-built chests that the player could open normally and that nobody is using.
    /// </summary>
    private static bool CanTakeFrom(Container container)
    {
        if (container.GetInventory() == null
            || container.m_wagon != null
            || container.m_autoDestroyEmpty
            || container.GetComponent<TombStone>() != null)
        {
            return false;
        }

        ZNetView nview = NviewRef(container);
        if (nview == null || !nview.IsValid())
        {
            return false;
        }

        if (container.GetComponent<Piece>() == null)
        {
            return false;
        }

        // IsInUse is only true on the client that owns the chest; everyone else sees the synced flag. Taking
        // from a chest another player has open would fight with their copy of its contents.
        if (container.IsInUse() || nview.GetZDO().GetInt(ZDOVars.s_inUse) == 1)
        {
            return false;
        }

        // The same privacy rules as opening the chest yourself.
        return container.CheckAccess(Game.instance.GetPlayerProfile().GetPlayerID())
            && (!container.m_checkGuardStone
            || PrivateArea.CheckAccess(container.transform.position, 0f, false));
    }

    private static void TakeFromChests(Player player, string name, int amount, int quality, bool matchWorldLevel)
    {
        amount = TakeFromGround(player, name, amount, quality, matchWorldLevel);
        foreach (Container container in GetNearby(player))
        {
            if (amount <= 0)
            {
                return;
            }

            Inventory inventory = container.GetInventory();
            int take = Mathf.Min(inventory.CountItems(name, quality, matchWorldLevel), amount);
            if (take <= 0)
            {
                continue;
            }

            // Only the owner's changes are saved, so take ownership of chests held by others.
            if (!container.IsOwner())
            {
                NviewRef(container).ClaimOwnership();
            }

            inventory.RemoveItem(name, take, quality, matchWorldLevel);
            amount -= take;
        }
    }

    [HarmonyPatch(typeof(ItemDrop), "Awake")]
    private static class ItemDrop_Awake_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ItemDrop __instance)
        {
            if (!Drops.Contains(__instance))
            {
                Drops.Add(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Container), "Awake")]
    private static class Container_Awake_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Container __instance)
        {
            if (__instance.GetInventory() != null && !Containers.Contains(__instance))
            {
                Containers.Add(__instance);
            }
        }
    }

    /// <summary>
    /// Build checks (can the piece be built / is its button greyed out) count chest contents too.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), typeof(Piece), typeof(Player.RequirementMode))]
    private static class Player_HaveRequirements_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Player __instance, Player.RequirementMode mode, out bool __state)
        {
            __state = mode == Player.RequirementMode.CanBuild && Active(__instance);
            if (__state)
            {
                _countDepth++;
            }
        }

        [HarmonyFinalizer]
        private static void Finalizer(bool __state)
        {
            if (__state)
            {
                _countDepth--;
            }
        }
    }

    /// <summary>
    /// The requirement list in the build panel shows the combined amount.
    /// </summary>
    [HarmonyPatch(typeof(Hud), "SetupPieceInfo")]
    private static class Hud_SetupPieceInfo_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(out bool __state)
        {
            __state = Active(Player.m_localPlayer);
            if (__state)
            {
                _countDepth++;
            }
        }

        /// <summary>
        /// Shows "total/required" for each material, where total covers the inventory, chests and ground items in range.
        /// </summary>
        [HarmonyPostfix]
        private static void Postfix(Hud __instance, Piece piece, bool __state)
        {
            if (!__state || piece == null)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            _countDepth++;
            _countAllGround = true;
            try
            {
                for (int i = 0; i < piece.m_resources.Length && i < __instance.m_requirementItems.Length; i++)
                {
                    Piece.Requirement req = piece.m_resources[i];
                    int need = req.GetAmount(0);
                    if (req.m_resItem == null || need <= 0)
                    {
                        continue;
                    }

                    Transform amountTransform = __instance.m_requirementItems[i].transform.Find("res_amount");
                    TMP_Text amountText = amountTransform != null ? amountTransform.GetComponent<TMP_Text>() : null;
                    if (amountText == null)
                    {
                        continue;
                    }

                    int total = player.GetInventory().CountItems(req.m_resItem.m_itemData.m_shared.m_name);
                    amountText.text = $"{total}/{need}";
                }
            }
            finally
            {
                _countDepth--;
                _countAllGround = false;
            }
        }

        [HarmonyFinalizer]
        private static void Finalizer(bool __state)
        {
            if (__state)
            {
                _countDepth--;
            }
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.CountItems))]
    private static class Inventory_CountItems_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(Inventory __instance, string name, int quality, bool matchWorldLevel, ref int __result)
        {
            Player player = Player.m_localPlayer;
            if (_countDepth <= 0 || name == null || !Active(player) || __instance != player.GetInventory())
            {
                return;
            }

            foreach (Container container in GetNearby(player))
            {
                __result += container.GetInventory().CountItems(name, quality, matchWorldLevel);
            }

            foreach (ItemDrop drop in GetGroundMatches(player, name, quality, matchWorldLevel))
            {
                if (_countAllGround || OwnedOrRequest(drop))
                {
                    __result += drop.m_itemData.m_stack;
                }
            }
        }
    }

    /// <summary>
    /// Only the resources of a piece being placed are drawn from chests, not crafting costs.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
    private static class Player_ConsumeResources_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Player __instance, Piece.Requirement[] requirements, out bool __state)
        {
            __state = Active(__instance)
                && __instance.InPlaceMode()
                && ((__instance.GetSelectedPiece() != null && __instance.GetSelectedPiece().m_resources == requirements)
                || (MultiCopy.ConsumingRequirements != null && MultiCopy.ConsumingRequirements == requirements));
            if (__state)
            {
                _consuming = true;
            }
        }

        [HarmonyFinalizer]
        private static void Finalizer(bool __state)
        {
            if (__state)
            {
                _consuming = false;
            }
        }
    }

    /// <summary>
    /// Uses the player's own stock first and takes only the shortfall from chests.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), typeof(string), typeof(int), typeof(int), typeof(bool))]
    private static class Inventory_RemoveItem_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(Inventory __instance, string name, ref int amount, int itemQuality, bool worldLevelBased)
        {
            Player player = Player.m_localPlayer;
            if (!_consuming || !Active(player) || __instance != player.GetInventory())
            {
                return true;
            }

            int have = __instance.CountItems(name, itemQuality, worldLevelBased);
            if (have >= amount)
            {
                return true;
            }

            TakeFromChests(player, name, amount - have, itemQuality, worldLevelBased);
            if (have <= 0)
            {
                return false;
            }

            amount = have;
            return true;
        }
    }
}
