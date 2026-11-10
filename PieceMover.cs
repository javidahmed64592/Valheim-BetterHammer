using System;
using System.Collections.Generic;

using UnityEngine;

using Object = UnityEngine.Object;

namespace NoHammerBuildMenu;

/// <summary>
/// <para>
/// Ctrl+middle-click picks up a built piece and shows its ghost, the same way Shift+middle-click copies
/// one. Left-click puts it down where the ghost is and right-click cancels.
/// </para>
/// <para>
/// Putting a piece down creates a new object at the ghost with a copy of the original's saved data
/// (chest contents, health, sign text, creator and so on), then removes the original without dropping
/// its materials or contents. A new object is needed because other players' games only read a built
/// piece's position when it first loads, so moving the existing one would leave it in the old spot for
/// them.
/// </para>
/// </summary>
internal static class PieceMover
{
    private const string RemoveButton = "Remove";
    private const string PlaceButton = "Attack";

    private static ZNetView _source;
    private static string _prefabName;
    private static Piece _previousPiece;
    private static int _previousRotation;
    private static bool _blockRemoveUntilReleased;
    private static int _rightClickFrame = -10;

    /// <summary>
    /// The same Ember Gold BenchLink uses to highlight linked stations.
    /// </summary>
    private static readonly Color HighlightColor = new(0.941f, 0.690f, 0.290f);
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    private static readonly List<Collider> SourceColliders = [];
    private static readonly List<Collider> HiddenColliders = [];
    private static StationExtension _hiddenExtension;

    private static readonly Dictionary<Player.PlacementStatus, string> StatusMessages = new()
    {
        [Player.PlacementStatus.NoBuildZone] = "$msg_nobuildzone",
        [Player.PlacementStatus.BlockedbyPlayer] = "$msg_blocked",
        [Player.PlacementStatus.PrivateZone] = "$msg_privatezone",
        [Player.PlacementStatus.MoreSpace] = "$msg_needspace",
        [Player.PlacementStatus.NoTeleportArea] = "$msg_noteleportarea",
        [Player.PlacementStatus.ExtensionMissingStation] = "$msg_extensionmissingstation",
        [Player.PlacementStatus.WrongBiome] = "$msg_wrongbiome",
        [Player.PlacementStatus.NeedCultivated] = "$msg_needcultivated",
        [Player.PlacementStatus.NeedDirt] = "$msg_needdirt",
        [Player.PlacementStatus.NotInDungeon] = "$msg_notindungeon",
        [Player.PlacementStatus.DeepSnow] = "$msg_snowtoodeep",
        [Player.PlacementStatus.NoSnow] = "$msg_nosnow",
    };

    internal static bool Moving => _source != null;

    /// <summary>
    /// Right-clicking while moving only cancels the move, so it mustn't also open the build menu or
    /// toggle repair mode or the grid.
    /// </summary>
    internal static bool RightClickConsumed => Moving || _rightClickFrame == Time.frameCount;

    /// <summary>
    /// Runs before Player.UpdatePlacement for the local player. Returns whether vanilla placing must be
    /// blocked this frame, which is the case whenever a move was in progress at the start of it.
    /// </summary>
    internal static bool BeforeUpdatePlacement(Player player, bool takeInput)
    {
        // Vanilla removes the hovered piece when the remove button is released, so keep it from
        // removing the piece that Ctrl+middle-click just picked up.
        if (_blockRemoveUntilReleased)
        {
            if (ZInput.GetButton(RemoveButton) || ZInput.GetButtonUp(RemoveButton))
            {
                player.m_blockRemove = true;
            }
            else
            {
                _blockRemoveUntilReleased = false;
            }
        }

        string abortReason = Moving ? AbortReason(player) : null;
        if (abortReason != null)
        {
            Stop(player);
            player.Message(MessageHud.MessageType.TopLeft, abortReason);
        }

        bool wasMoving = Moving;
        if (!takeInput
            || !player.InPlaceMode()
            || player.IsDead()
            || Hud.IsPieceSelectionVisible()
            || Hud.InRadial()
            || ZInput.IsGamepadActive()
            || !NoHammerBuildMenuPlugin.IsHammerEquipped(player))
        {
            return wasMoving;
        }

        if (NoHammerBuildMenuPlugin.CtrlHeld() && ZInput.GetButtonDown(RemoveButton))
        {
            _blockRemoveUntilReleased = true;
            player.m_blockRemove = true;
            PickUp(player);
        }
        else if (Moving && ZInput.GetButtonDown(RemoveButton) && player.GetHoveringPiece() == _source.GetComponent<Piece>())
        {
            // Removing the piece being moved would drop its materials and (for a public chest) its
            // contents, so it's ignored. Other pieces can still be removed while moving.
            _blockRemoveUntilReleased = true;
            player.m_blockRemove = true;
            player.Message(MessageHud.MessageType.Center, "$nhbm_move_cant_remove");
        }
        else if (Moving && Input.GetMouseButtonDown(1))
        {
            _rightClickFrame = Time.frameCount;
            Stop(player);
            player.Message(MessageHud.MessageType.TopLeft, "$nhbm_move_aborted");
        }
        else if (Moving && ZInput.GetButtonDown(PlaceButton) && Time.time - player.m_lastToolUseTime > player.m_placeDelay)
        {
            PutDown(player);
        }

        return wasMoving || Moving;
    }

    /// <summary>
    /// Vanilla's place input would place a copy of the piece at a cost, so swallow it while moving.
    /// </summary>
    internal static void BlockVanillaPlacing(Player player)
    {
        // The prefix pushed the last tool use to "now" (so vanilla's delay check fails this frame),
        // this drops the click vanilla buffered for the following frames.
        player.m_placePressedTime = -9999f;
    }

    internal static void Reset()
    {
        ClearHighlight();
        _source = null;
        _prefabName = null;
        _previousPiece = null;
        _blockRemoveUntilReleased = false;
    }

    private static void PickUp(Player player)
    {
        // The piece vanilla highlights: the same ray and reach as copying and removing.
        Piece piece = player.GetHoveringPiece();
        if (piece == null || !CanMove(player, piece, out ZNetView nview))
        {
            return;
        }

        if (!Moving)
        {
            _previousPiece = player.GetSelectedPiece();
            _previousRotation = player.m_placeRotation;
        }

        if (!player.SetSelectedPiece(piece))
        {
            // Same as copying: the piece isn't one this hammer can build, or you don't know it yet.
            player.Message(MessageHud.MessageType.Center, "$msg_missingrequirement");
            return;
        }

        player.m_placeRotation = (int)Math.Round(piece.transform.rotation.eulerAngles.y / player.m_placeRotationDegrees);
        ClearHighlight();
        _source = nview;
        _prefabName = Utils.GetPrefabName(piece.gameObject);
        player.Message(MessageHud.MessageType.TopLeft,
            Localization.instance.Localize("$nhbm_move_started", Localization.instance.Localize(piece.m_name)));
    }

    private static void PutDown(Player player)
    {
        // Whatever happens, this click is used up; the delay also stops vanilla placing this frame.
        player.m_lastToolUseTime = Time.time;

        Piece piece = _source.GetComponent<Piece>();
        if (piece == null || !CanMove(player, piece, out _))
        {
            return;
        }

        ItemDrop.ItemData tool = player.GetRightItem();
        if (!player.HaveStamina(tool.m_shared.m_attack.m_attackStamina))
        {
            Hud.instance.StaminaBarEmptyFlash();
            return;
        }

        player.UpdatePlacementGhost(flashGuardStone: true);
        GameObject ghost = player.m_placementGhost;
        if (player.m_placementStatus != Player.PlacementStatus.Valid || ghost == null)
        {
            player.Message(MessageHud.MessageType.Center,
                StatusMessages.TryGetValue(player.m_placementStatus, out string message) ? message : "$msg_invalidplacement");
            return;
        }

        Vector3 oldPosition = piece.transform.position;
        Vector3 position = ghost.transform.position;
        Quaternion rotation = ghost.transform.rotation;
        GameObject moved = Recreate(_source, position, rotation);
        if (moved == null)
        {
            player.Message(MessageHud.MessageType.Center, "$nhbm_move_failed");
            return;
        }

        // Removing the original only after the copy exists means a failure can't lose a chest's items.
        ClearHighlight();
        _source.ClaimOwnership();
        ZNetScene.instance.Destroy(_source.gameObject);

        _ = piece.m_placeEffect.Create(position, rotation, moved.transform, 1f, -1, player.GetZDOID());
        _ = player.m_removeEffects.Create(oldPosition, Quaternion.identity, null, 1f, -1, player.GetZDOID());
        player.FaceLookDirection();
        player.m_zanim.SetTrigger(tool.m_shared.m_attack.m_attackAnimation);
        player.UseStamina(player.GetBuildStamina());
        if (tool.m_shared.m_useDurability)
        {
            tool.m_durability -= player.GetPlaceDurability(tool) * Game.m_durabilityRate;
        }

        _source = null;
        Stop(player);
        player.Message(MessageHud.MessageType.TopLeft,
            Localization.instance.Localize("$nhbm_moved", Localization.instance.Localize(piece.m_name)));
    }

    /// <summary>
    /// Makes the piece being moved glow, pulsing, so it's easy to keep track of. Runs in LateUpdate so it
    /// wins over the game's own hover highlight, which resets the glow from an Invoke after Update.
    /// </summary>
    internal static void UpdateHighlight()
    {
        if (!Moving || MaterialMan.instance == null)
        {
            return;
        }

        float pulse = Mathf.Lerp(0.3f, 0.7f, (Mathf.Sin(Time.time * 5f) + 1f) * 0.5f);
        MaterialMan.instance.SetValue(_source.gameObject, EmissionColor, HighlightColor * pulse);
    }

    private static void ClearHighlight()
    {
        if (_source != null && MaterialMan.instance != null)
        {
            MaterialMan.instance.ResetValue(_source.gameObject, EmissionColor);
        }
    }

    /// <summary>
    /// Lets the piece be put down overlapping where it is now, so it can be nudged a little. While the
    /// game checks the ghost's spot, the original's colliders are switched off (so the aim ray, snapping,
    /// clipping and blocking checks don't see it) and, for a workbench extension, it's taken out of the
    /// list used for the "too close to another extension" check. Both are put back straight afterwards,
    /// within the same frame, before physics runs.
    /// </summary>
    internal static void HideFromPlacementChecks()
    {
        if (!Moving)
        {
            return;
        }

        _source.GetComponentsInChildren(SourceColliders);
        foreach (Collider collider in SourceColliders)
        {
            if (collider.enabled)
            {
                collider.enabled = false;
                HiddenColliders.Add(collider);
            }
        }

        SourceColliders.Clear();

        StationExtension extension = _source.GetComponentInChildren<StationExtension>();
        if (extension != null && StationExtension.m_allExtensions.Remove(extension))
        {
            _hiddenExtension = extension;
        }
    }

    internal static void RestoreAfterPlacementChecks()
    {
        foreach (Collider collider in HiddenColliders)
        {
            if (collider != null)
            {
                collider.enabled = true;
            }
        }

        HiddenColliders.Clear();

        if (_hiddenExtension != null)
        {
            StationExtension.m_allExtensions.Add(_hiddenExtension);
            _hiddenExtension = null;
        }
    }

    /// <summary>
    /// Creates a new object for the source's prefab at the given spot, with a copy of the source's data.
    /// Mirrors how ZNetScene creates objects that load from the network.
    /// </summary>
    private static GameObject Recreate(ZNetView source, Vector3 position, Quaternion rotation)
    {
        ZDO old = source.GetZDO();
        int prefabHash = old.GetPrefab();
        GameObject prefab = ZNetScene.instance.GetPrefab(prefabHash);
        if (prefab == null)
        {
            return null;
        }

        ZDO zdo = ZDOMan.instance.CreateNewZDO(position, prefabHash);
        zdo.Persistent = old.Persistent;
        zdo.Type = old.Type;
        zdo.Distant = old.Distant;
        zdo.SetPrefab(prefabHash);
        zdo.SetRotation(rotation);
        CopyData(old.m_uid, zdo);

        GameObject created = null;
        ZNetView.m_useInitZDO = true;
        ZNetView.m_initZDO = zdo;
        try
        {
            created = Object.Instantiate(prefab, position, rotation);
        }
        catch (Exception e)
        {
            Debug.LogError("NoHammerBuildMenu: failed to move " + prefab.name + ": " + e);
        }
        finally
        {
            bool unused = ZNetView.m_initZDO != null;
            ZNetView.m_initZDO = null;
            ZNetView.m_useInitZDO = false;
            if (created == null || unused)
            {
                // Don't leave a second copy of the piece (and its contents) in the world.
                if (created != null)
                {
                    Object.Destroy(created);
                }

                ZDOMan.instance.DestroyZDO(zdo);
                created = null;
            }
        }

        return created;
    }

    /// <summary>
    /// Copies every value the source has saved. Connections (such as a portal's link) are left out:
    /// they point from the other object to the old one, and portals relink by tag on their own.
    /// </summary>
    private static void CopyData(ZDOID from, ZDO to)
    {
        ZDOExtraData.GetData(from,
            out List<KeyValuePair<int, float>> floats,
            out List<KeyValuePair<int, Vector3>> vec3s,
            out List<KeyValuePair<int, Quaternion>> quats,
            out List<KeyValuePair<int, int>> ints,
            out List<KeyValuePair<int, long>> longs,
            out List<KeyValuePair<int, string>> strings,
            out List<KeyValuePair<int, byte[]>> byteArrays,
            out _);

        foreach (KeyValuePair<int, float> pair in floats)
        {
            to.Set(pair.Key, pair.Value);
        }

        foreach (KeyValuePair<int, Vector3> pair in vec3s)
        {
            to.Set(pair.Key, pair.Value);
        }

        foreach (KeyValuePair<int, Quaternion> pair in quats)
        {
            to.Set(pair.Key, pair.Value);
        }

        foreach (KeyValuePair<int, int> pair in ints)
        {
            to.Set(pair.Key, pair.Value);
        }

        foreach (KeyValuePair<int, long> pair in longs)
        {
            to.Set(pair.Key, pair.Value);
        }

        foreach (KeyValuePair<int, string> pair in strings)
        {
            to.Set(pair.Key, pair.Value);
        }

        foreach (KeyValuePair<int, byte[]> pair in byteArrays)
        {
            to.Set(pair.Key, (byte[])pair.Value.Clone());
        }
    }

    /// <summary>
    /// The same rules as removing the piece with the hammer, plus the chest rules for opening it.
    /// Shows the reason when the piece can't be moved.
    /// </summary>
    private static bool CanMove(Player player, Piece piece, out ZNetView nview)
    {
        nview = piece.GetComponent<ZNetView>();
        if (nview == null || !nview.IsValid() || !piece.m_canBeRemoved || !IsMovableKind(piece))
        {
            player.Message(MessageHud.MessageType.Center, "$nhbm_move_cant");
            return false;
        }

        Vector3 position = piece.transform.position;
        if (Location.IsInsideNoBuildLocation(position))
        {
            player.Message(MessageHud.MessageType.Center, "$msg_nobuildzone");
            return false;
        }

        if (!PrivateArea.CheckAccess(position))
        {
            player.Message(MessageHud.MessageType.Center, "$msg_privatezone");
            return false;
        }

        if (!player.CheckCanRemovePiece(piece))
        {
            return false; // shows "$msg_missingstation"
        }

        Container container = piece.GetComponentInChildren<Container>();
        if (container != null)
        {
            if (!container.CheckAccess(Game.instance.GetPlayerProfile().GetPlayerID()))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_cantopen");
                return false;
            }

            // IsInUse is only set on the chest owner's game; everyone else sees the synced flag.
            if (container.IsInUse() || nview.GetZDO().GetInt(ZDOVars.s_inUse) == 1)
            {
                player.Message(MessageHud.MessageType.Center, "$msg_inuse");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Only plain built objects: not ships, carts, creatures, items placed as pieces, terrain changes, or
    /// anything made of more than one networked object.
    /// </summary>
    private static bool IsMovableKind(Piece piece)
    {
        return piece.GetComponentsInChildren<ZNetView>(true).Length == 1
            && piece.GetComponentInChildren<Ship>(true) == null
            && piece.GetComponentInChildren<Vagon>(true) == null
            && piece.GetComponentInChildren<Character>(true) == null
            && piece.GetComponentInChildren<ItemDrop>(true) == null
            && piece.GetComponentInChildren<TerrainModifier>(true) == null
            && piece.GetComponentInChildren<IRemoved>(true) == null;
    }

    /// <summary>
    /// Why the move has to end, or null while it can carry on: the original is gone (removed by
    /// someone else or destroyed), the hammer was put away, or a different piece got selected.
    /// </summary>
    private static string AbortReason(Player player)
    {
        bool gone = _source == null || !_source.IsValid();
        bool unequipped = !player.InPlaceMode() || !NoHammerBuildMenuPlugin.IsHammerEquipped(player);
        return (gone, unequipped) switch
        {
            (true, _) => "$nhbm_move_aborted_gone",
            (_, true) => "$nhbm_move_aborted_unequipped",
            _ when !SelectedIsMovedPiece(player) => "$nhbm_move_aborted_selection",
            _ => null,
        };
    }

    private static bool SelectedIsMovedPiece(Player player)
    {
        Piece selected = player.GetSelectedPiece();
        return selected != null && Utils.GetPrefabName(selected.gameObject) == _prefabName;
    }

    /// <summary>
    /// Ends the move and, if the moved piece is still the selected one, goes back to the piece and
    /// rotation selected before it.
    /// </summary>
    private static void Stop(Player player)
    {
        if (player.InPlaceMode() && SelectedIsMovedPiece(player))
        {
            if (_previousPiece != null)
            {
                _ = player.SetSelectedPiece(_previousPiece);
            }

            player.m_placeRotation = _previousRotation;
        }

        ClearHighlight();
        _source = null;
        _prefabName = null;
        _previousPiece = null;
    }
}
