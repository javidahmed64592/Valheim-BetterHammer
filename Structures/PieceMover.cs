using System;
using System.Collections.Generic;
using System.Globalization;

using UnityEngine;

using Object = UnityEngine.Object;

namespace BetterHammer.Structures;

/// <summary>
/// <para>
/// Ctrl+middle-click picks up a built piece and shows its ghost, the same way Shift+middle-click copies
/// one. Left-click puts it down where the ghost is and right-click cancels. Ctrl+middle-click on a piece
/// selected for copying along with others picks up the whole selection instead, shown as a
/// <see cref="GhostGroup"/> like a saved copy.
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

    /// <summary>
    /// The pieces being moved; the first is the one that was clicked, and the reference piece to start with.
    /// </summary>
    private static readonly List<ZNetView> Sources = [];
    private static string _prefabName;
    private static Piece _previousPiece;
    private static int _previousRotation;
    private static bool _blockRemoveUntilReleased;
    private static int _rightClickFrame = -10;

    /// <summary>
    /// A move is paused while the hammer is in repair mode, so the pieces can be repaired first, and
    /// carries on when the moved piece is selected again. These are what it carries on with.
    /// </summary>
    private static bool _paused;
    private static int _pausedRotation;
    private static int _pausedSnapPoint;
    private static int _pausedReference;

    /// <summary>
    /// The group's reference piece just before the game last set up its ghost, which ends the group.
    /// </summary>
    private static int _groupReference;

    /// <summary>
    /// The same Ember Gold BenchLink uses to highlight linked stations.
    /// </summary>
    private static readonly Color HighlightColor = new(0.941f, 0.690f, 0.290f);
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    private static readonly List<Collider> SourceColliders = [];
    private static readonly List<Collider> HiddenColliders = [];
    private static readonly List<StationExtension> HiddenExtensions = [];

    internal static readonly Dictionary<Player.PlacementStatus, string> StatusMessages = new()
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

    internal static bool Moving => Sources.Count > 0;

    private static bool MovingGroup => Sources.Count > 1;

    /// <summary>
    /// Right-clicking while moving cancels the move (or, with Shift or Ctrl, toggles repair mode or the
    /// grid), so it mustn't also open the build menu.
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

        if (Moving)
        {
            UpdatePause(player);
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
            || !BetterHammerPlugin.IsHammerEquipped(player))
        {
            return wasMoving;
        }

        if (BetterHammerPlugin.CtrlHeld() && ZInput.GetButtonDown(RemoveButton))
        {
            BlockRemoveUntilReleased(player);
            PickUp(player);
        }
        else if (Moving && ZInput.GetButtonDown(RemoveButton) && IsSource(player.GetHoveringPiece()))
        {
            // Removing a piece being moved would drop its materials and (for a public chest) its
            // contents, so it's ignored. Other pieces can still be removed while moving.
            BlockRemoveUntilReleased(player);
            player.Message(MessageHud.MessageType.Center, "$betterhammer_move_cant_remove");
        }
        else if (Moving && Input.GetMouseButtonDown(1) && !BetterHammerPlugin.CtrlHeld() && !BetterHammerPlugin.ShiftHeld())
        {
            // Ctrl+ and Shift+right-click toggle the grid and repair mode instead.
            _rightClickFrame = Time.frameCount;
            Stop(player);
            player.Message(MessageHud.MessageType.TopLeft, "$betterhammer_move_aborted");
        }
        else if (Moving && !_paused && !BetterHammerPlugin.AltHeld() && ZInput.GetButtonDown(PlaceButton) && Time.time - player.m_lastToolUseTime > player.m_placeDelay)
        {
            PutDown(player);
        }

        // While paused, left-click repairs as normal.
        return (wasMoving || Moving) && !_paused;
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

    /// <summary>
    /// Vanilla removes the hovered piece when the remove button is released. Called when a modified
    /// middle-click (or one on the piece being moved) is used for something else, so it doesn't.
    /// </summary>
    internal static void BlockRemoveUntilReleased(Player player)
    {
        _blockRemoveUntilReleased = true;
        player.m_blockRemove = true;
    }

    /// <summary>
    /// Ends a move in progress because another mode (copying a selection) is starting.
    /// </summary>
    internal static void Abort(Player player)
    {
        if (Moving)
        {
            Stop(player);
            player.Message(MessageHud.MessageType.TopLeft, "$betterhammer_move_aborted");
        }
    }

    internal static void Reset()
    {
        ClearHighlight();
        Sources.Clear();
        _paused = false;
        _prefabName = null;
        _previousPiece = null;
        _blockRemoveUntilReleased = false;
    }

    private static void PickUp(Player player)
    {
        // The piece vanilla highlights: the same ray and reach as copying and removing.
        Piece piece = player.GetHoveringPiece();
        if (piece == null)
        {
            return;
        }

        // A piece selected along with others brings the whole selection.
        List<Piece> pieces = MultiCopy.SelectionStartingWith(piece) ?? [piece];
        List<ZNetView> nviews = [];
        foreach (Piece moving in pieces)
        {
            if (!CanMove(player, moving, out ZNetView nview))
            {
                return;
            }

            nviews.Add(nview);
        }

        if (!Moving)
        {
            _previousPiece = player.GetSelectedPiece();
            _previousRotation = player.m_placeRotation;
        }

        // A group's reference ghost may be another of its pieces than the selected one (after Shift+Q/E),
        // so the game's ghost is set up again rather than kept.
        bool hadGroup = GhostGroup.ActiveOwner == GhostGroup.Owner.Move;
        if (hadGroup)
        {
            GhostGroup.End();
        }

        if (!player.SetSelectedPiece(piece))
        {
            // Same as copying: the piece isn't one this hammer can build, or you don't know it yet.
            player.Message(MessageHud.MessageType.Center, "$msg_missingrequirement");
            return;
        }

        player.m_placeRotation = (int)Math.Round(piece.transform.rotation.eulerAngles.y / player.m_placeRotationDegrees);
        _paused = false;
        ClearHighlight();
        Sources.Clear();
        Sources.AddRange(nviews);
        _prefabName = Utils.GetPrefabName(piece.gameObject);

        if (!MovingGroup)
        {
            if (hadGroup)
            {
                player.SetupPlacementGhost();
            }

            player.Message(MessageHud.MessageType.TopLeft,
                Localization.instance.Localize("$betterhammer_move_started", Localization.instance.Localize(piece.m_name)));
            return;
        }

        List<CopyLibrary.Part> parts = [];
        foreach (Piece moving in pieces)
        {
            parts.Add(CopyLibrary.MakePart(PrefabOf(moving), moving.transform.position, moving.transform.rotation));
        }

        GhostGroup.Begin(player, parts, GhostGroup.Owner.Move);
        player.Message(MessageHud.MessageType.TopLeft,
            Localization.instance.Localize("$betterhammer_move_started_group", Sources.Count.ToString(CultureInfo.InvariantCulture)));
    }

    private static Piece PrefabOf(Piece piece)
    {
        GameObject prefab = ZNetScene.instance.GetPrefab(Utils.GetPrefabName(piece.gameObject));
        return prefab != null ? prefab.GetComponent<Piece>() : piece;
    }

    private static bool IsSource(Piece piece)
    {
        if (piece == null)
        {
            return false;
        }

        foreach (ZNetView source in Sources)
        {
            if (source != null && source.GetComponent<Piece>() == piece)
            {
                return true;
            }
        }

        return false;
    }

    private static void PutDown(Player player)
    {
        // Whatever happens, this click is used up; the delay also stops vanilla placing this frame.
        player.m_lastToolUseTime = Time.time;

        List<Piece> pieces = [];
        foreach (ZNetView source in Sources)
        {
            Piece piece = source.GetComponent<Piece>();
            if (piece == null || !CanMove(player, piece, out _))
            {
                return;
            }

            pieces.Add(piece);
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

        List<(Vector3 Position, Quaternion Rotation)> poses = MovingGroup
            ? GhostGroup.Poses(player)
            : [(ghost.transform.position, ghost.transform.rotation)];

        // Every new object is made before any original is removed, so a failure can't lose a chest's
        // items: if one can't be made, the ones made so far are removed again and nothing moves.
        List<GameObject> moved = [];
        for (int i = 0; i < Sources.Count; i++)
        {
            GameObject created = Recreate(Sources[i], poses[i].Position, poses[i].Rotation);
            if (created == null)
            {
                foreach (GameObject undo in moved)
                {
                    ZNetScene.instance.Destroy(undo);
                }

                player.Message(MessageHud.MessageType.Center, "$betterhammer_move_failed");
                return;
            }

            moved.Add(created);
        }

        Vector3 oldPosition = pieces[0].transform.position;
        ClearHighlight();
        for (int i = 0; i < Sources.Count; i++)
        {
            // A piece selected for copying stays selected, at its new spot.
            MultiCopy.ReplaceSelected(pieces[i], moved[i].GetComponent<Piece>());
            Sources[i].ClaimOwnership();
            ZNetScene.instance.Destroy(Sources[i].gameObject);
            _ = pieces[i].m_placeEffect.Create(poses[i].Position, poses[i].Rotation, moved[i].transform, 1f, -1, player.GetZDOID());
        }

        _ = player.m_removeEffects.Create(oldPosition, Quaternion.identity, null, 1f, -1, player.GetZDOID());
        player.FaceLookDirection();
        player.m_zanim.SetTrigger(tool.m_shared.m_attack.m_attackAnimation);
        player.UseStamina(player.GetBuildStamina());
        if (tool.m_shared.m_useDurability)
        {
            tool.m_durability -= player.GetPlaceDurability(tool) * Game.m_durabilityRate;
        }

        int count = Sources.Count;
        Sources.Clear();
        Stop(player);
        player.Message(MessageHud.MessageType.TopLeft, count > 1
            ? Localization.instance.Localize("$betterhammer_moved_group", count.ToString(CultureInfo.InvariantCulture))
            : Localization.instance.Localize("$betterhammer_moved", Localization.instance.Localize(pieces[0].m_name)));
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
        foreach (ZNetView source in Sources)
        {
            if (source != null)
            {
                MaterialMan.instance.SetValue(source.gameObject, EmissionColor, HighlightColor * pulse);
            }
        }
    }

    private static void ClearHighlight()
    {
        if (MaterialMan.instance == null)
        {
            return;
        }

        foreach (ZNetView source in Sources)
        {
            if (source != null)
            {
                MaterialMan.instance.ResetValue(source.gameObject, EmissionColor);
            }
        }
    }

    /// <summary>
    /// Lets the pieces be put down overlapping where they are now, so they can be nudged a little. While
    /// the game checks the ghosts' spots, the originals' colliders are switched off (so the aim ray, snapping,
    /// clipping and blocking checks don't see it) and, for a workbench extension, it's taken out of the
    /// list used for the "too close to another extension" check. Both are put back straight afterwards,
    /// within the same frame, before physics runs.
    /// </summary>
    internal static void HideFromPlacementChecks()
    {
        // While paused, the repair hammer must be able to aim at the pieces.
        if (!Moving || _paused)
        {
            return;
        }

        foreach (ZNetView source in Sources)
        {
            if (source == null)
            {
                continue;
            }

            source.GetComponentsInChildren(SourceColliders);
            foreach (Collider collider in SourceColliders)
            {
                if (collider.enabled)
                {
                    collider.enabled = false;
                    HiddenColliders.Add(collider);
                }
            }

            SourceColliders.Clear();

            StationExtension extension = source.GetComponentInChildren<StationExtension>();
            if (extension != null && StationExtension.m_allExtensions.Remove(extension))
            {
                HiddenExtensions.Add(extension);
            }
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

        StationExtension.m_allExtensions.AddRange(HiddenExtensions);
        HiddenExtensions.Clear();
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
            Debug.LogError("BetterHammer: failed to move " + prefab.name + ": " + e);
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
            player.Message(MessageHud.MessageType.Center, "$betterhammer_move_cant");
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
    /// Why the move has to end, or null while it can carry on: an original is gone (removed by someone
    /// else or destroyed), the hammer was put away, or a different piece got selected.
    /// </summary>
    /// <summary>
    /// Runs before Player.SetupPlacementGhost: notes a moved group's reference piece, since setting up
    /// the ghost ends the group.
    /// </summary>
    internal static void BeforeSetupPlacementGhost()
    {
        if (Moving && !GhostGroup.CreatingGhosts && GhostGroup.ActiveOwner == GhostGroup.Owner.Move)
        {
            _groupReference = GhostGroup.Reference;
        }
    }

    /// <summary>
    /// Pauses the move when repair mode is turned on, and carries on with it when repair mode is turned
    /// off again with the moved piece selected. Leaving repair mode for any other piece ends the move
    /// (see <see cref="AbortReason"/>).
    /// </summary>
    private static void UpdatePause(Player player)
    {
        bool repairing = player.InPlaceMode() && player.InRepairMode();
        if (!_paused && repairing)
        {
            _paused = true;
            _pausedRotation = player.m_placeRotation;
            _pausedSnapPoint = player.m_manualSnapPoint;
            _pausedReference = MovingGroup ? _groupReference : 0;
            if (GhostGroup.ActiveOwner == GhostGroup.Owner.Move)
            {
                GhostGroup.End();
            }

            player.Message(MessageHud.MessageType.TopLeft, "$betterhammer_move_paused");
        }
        else if (_paused && !repairing && player.InPlaceMode())
        {
            _paused = false;
            if (SelectedIsMovedPiece(player))
            {
                Resume(player);
            }
        }
    }

    /// <summary>
    /// Sets the move's ghost up again after a pause, turned and snapping as it was.
    /// </summary>
    private static void Resume(Player player)
    {
        // Selecting a piece that starts at a random turn re-rolls the build rotation.
        player.m_placeRotation = _pausedRotation;
        if (MovingGroup)
        {
            List<CopyLibrary.Part> parts = [];
            foreach (ZNetView source in Sources)
            {
                Piece piece = source.GetComponent<Piece>();
                parts.Add(CopyLibrary.MakePart(PrefabOf(piece), piece.transform.position, piece.transform.rotation));
            }

            GhostGroup.Begin(player, parts, GhostGroup.Owner.Move, _pausedReference, _pausedSnapPoint);
        }
        else
        {
            player.m_manualSnapPoint = _pausedSnapPoint;
        }

        player.Message(MessageHud.MessageType.TopLeft, "$betterhammer_move_resumed");
    }

    private static string AbortReason(Player player)
    {
        bool gone = Sources.Exists(source => source == null || !source.IsValid());
        bool unequipped = !player.InPlaceMode() || !BetterHammerPlugin.IsHammerEquipped(player);
        return (gone, unequipped) switch
        {
            (true, _) => "$betterhammer_move_aborted_gone",
            (_, true) => "$betterhammer_move_aborted_unequipped",
            _ when _paused => null,
            _ when !SelectedIsMovedPiece(player) => "$betterhammer_move_aborted_selection",

            // The game set up its ghost again, taking the group's reference ghost with it.
            _ when MovingGroup && GhostGroup.ActiveOwner != GhostGroup.Owner.Move => "$betterhammer_move_aborted",
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
        bool hadGroup = GhostGroup.ActiveOwner == GhostGroup.Owner.Move;
        if (hadGroup)
        {
            GhostGroup.End();
        }

        if (player.InPlaceMode() && SelectedIsMovedPiece(player))
        {
            if (_previousPiece != null)
            {
                _ = player.SetSelectedPiece(_previousPiece);
            }

            player.m_placeRotation = _previousRotation;

            // The game's ghost may still be the group's reference ghost, possibly of another piece than
            // the selected one, so set it up again (this also brings back a saved copy's ghosts).
            if (hadGroup)
            {
                player.SetupPlacementGhost();
            }
        }
        else if (_paused)
        {
            // Still in repair mode: leaving it goes back to the piece selected before the move.
            if (_previousPiece != null)
            {
                BetterHammerPlugin.SetRepairReturnPiece(_previousPiece);
            }

            player.m_placeRotation = _previousRotation;
        }

        _paused = false;
        ClearHighlight();
        Sources.Clear();
        _prefabName = null;
        _previousPiece = null;
    }
}
