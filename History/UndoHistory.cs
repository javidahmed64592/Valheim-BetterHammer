using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using BepInEx.Configuration;

using BetterHammer.Structures;

using UnityEngine;

namespace BetterHammer.History;

/// <summary>
/// <para>
/// Ctrl+Z undoes the last build action and Ctrl+Y redoes it: building a piece or a saved copy, moving a
/// piece or a selection, and removing a piece or a selection (with the hammer, Delete or Cut). The history
/// is kept for this session only.
/// </para>
/// <para>
/// Undoing and redoing follow the same rules as doing it by hand, so they can't make materials from
/// nothing: undoing a build removes the pieces the way the hammer does (their materials drop where they
/// stood), and undoing a removal builds the pieces again at the usual cost (the dropped materials count if
/// they're nearby). Moving is free either way. Nothing happens unless every piece can be dealt with.
/// </para>
/// </summary>
internal static class UndoHistory
{
    private enum Kind { Build = 0, Remove = 1, Move = 2 }

    /// <summary>
    /// A piece the history refers to. Moving or rebuilding a piece makes a new object, so every entry
    /// shares one handle per piece and its id follows the piece.
    /// </summary>
    private sealed class Handle
    {
        public ZDOID Id;
        public Piece Prefab;

        /// <summary>
        /// The piece was destroyed (by anyone), so there's nothing left to remove or move.
        /// </summary>
        public bool Destroyed;
    }

    private sealed class Entry
    {
        public Kind Kind;
        public List<Handle> Pieces;

        /// <summary>
        /// Where each piece stands when built (Build, Remove), or stood before the move (Move).
        /// </summary>
        public List<(Vector3 Position, Quaternion Rotation)> Poses;

        /// <summary>
        /// Where each piece was moved to (Move only).
        /// </summary>
        public List<(Vector3 Position, Quaternion Rotation)> MovedTo;
    }

    private enum Outcome { Done = 0, Blocked = 1, Gone = 2 }

    private static readonly List<Entry> Undos = [];
    private static readonly List<Entry> Redos = [];
    private static readonly Dictionary<ZDOID, Handle> Handles = [];

    /// <summary>
    /// The pieces placed by Player.PlacePiece while it's being watched (see <see cref="BeforePlacePiece"/>).
    /// </summary>
    private static readonly List<Piece> Placed = [];
    private static int _placeDepth;
    private static int _batchDepth;
    private static bool _replaying;
    private static bool _removing;
    private static Entry _pendingRemove;

    /// <summary>
    /// A stand-in piece carrying the combined materials of the pieces being rebuilt, for the game's own
    /// material check (which the chest materials feature extends).
    /// </summary>
    private static Piece _costHolder;

    private static ConfigEntry<KeyboardShortcut> _undoKey;
    private static ConfigEntry<KeyboardShortcut> _redoKey;
    private static ConfigEntry<int> _size;

    internal static KeyboardShortcut UndoKey => _undoKey != null ? _undoKey.Value : new KeyboardShortcut(KeyCode.Z, KeyCode.LeftControl);
    internal static KeyboardShortcut RedoKey => _redoKey != null ? _redoKey.Value : new KeyboardShortcut(KeyCode.Y, KeyCode.LeftControl);

    internal static bool CanUndo => Undos.Count > 0;
    internal static bool CanRedo => Redos.Count > 0;

    public static void BindConfig(ConfigFile config)
    {
        _undoKey = config.Bind(
            "Keybinds",
            "Undo",
            new KeyboardShortcut(KeyCode.Z, KeyCode.LeftControl),
            "Undoes the last build, move or removal made with the hammer.");
        _redoKey = config.Bind(
            "Keybinds",
            "Redo",
            new KeyboardShortcut(KeyCode.Y, KeyCode.LeftControl),
            "Redoes the last build, move or removal that was undone.");
        _size = config.Bind(
            "Building",
            "Undo History",
            50,
            new ConfigDescription("How many build actions can be undone.", new AcceptableValueRange<int>(1, 500)));
    }

    internal static void Reset()
    {
        Undos.Clear();
        Redos.Clear();
        Handles.Clear();
        Placed.Clear();
        _placeDepth = 0;
        _batchDepth = 0;
        _replaying = false;
        _removing = false;
        _pendingRemove = null;
    }

    /// <summary>
    /// With a hammer out and nothing else going on: the undo and redo keys.
    /// </summary>
    internal static void UpdateKeys(Player player)
    {
        if (!player.InPlaceMode()
            || player.IsDead()
            || Hud.IsPieceSelectionVisible()
            || UnifiedPopup.IsVisible()
            || CopyLibrary.TypingInTextField()
            || PieceMover.Moving
            || !BetterHammerPlugin.IsHammerEquipped(player))
        {
            return;
        }

        if (UndoKey.IsDown())
        {
            Step(player, undo: true);
        }
        else if (RedoKey.IsDown())
        {
            Step(player, undo: false);
        }
    }

    // ----- Recording -----

    /// <summary>
    /// Runs before Player.PlacePiece for the local player: starts noting the pieces it places.
    /// </summary>
    internal static void BeforePlacePiece()
    {
        _placeDepth++;
    }

    /// <summary>
    /// Runs after Player.PlacePiece: a piece built on its own (not part of a saved copy, or rebuilt by
    /// undo or redo) is a build action of its own.
    /// </summary>
    internal static void AfterPlacePiece()
    {
        _placeDepth = Mathf.Max(0, _placeDepth - 1);
        if (_placeDepth == 0 && _batchDepth == 0 && !_replaying)
        {
            RecordBuilt();
        }
    }

    /// <summary>
    /// Runs after Piece.SetCreator, which Player.PlacePiece calls on the piece it just made.
    /// </summary>
    internal static void OnCreatorSet(Piece piece)
    {
        if (_placeDepth > 0 && piece != null)
        {
            Placed.Add(piece);
        }
    }

    /// <summary>
    /// Every piece placed until <see cref="EndBuild"/> is one build action (a saved copy).
    /// </summary>
    internal static void BeginBuild()
    {
        _batchDepth++;
    }

    internal static void EndBuild()
    {
        _batchDepth = Mathf.Max(0, _batchDepth - 1);
        if (_batchDepth == 0 && !_replaying)
        {
            RecordBuilt();
        }
    }

    private static void RecordBuilt()
    {
        List<Piece> pieces = [.. Placed.Where(piece => piece != null)];
        Placed.Clear();
        if (pieces.Count == 0)
        {
            return;
        }

        Entry entry = Describe(Kind.Build, pieces);
        if (entry != null)
        {
            Push(entry);
        }
    }

    /// <summary>
    /// Call just before the pieces are removed, while they're still there to note.
    /// </summary>
    internal static void RecordRemoving(IEnumerable<Piece> pieces)
    {
        if (_replaying)
        {
            return;
        }

        Entry entry = Describe(Kind.Remove, pieces);
        if (entry != null)
        {
            Push(entry);
        }
    }

    /// <summary>
    /// Runs before Player.RemovePiece: watches for the piece it's about to remove, if the hammer is
    /// removing it for the local player.
    /// </summary>
    internal static void BeforeRemovePiece(Player player)
    {
        _removing = player == Player.m_localPlayer && BetterHammerPlugin.IsHammerEquipped(player);
        _pendingRemove = null;
    }

    /// <summary>
    /// Runs when Player.RemovePiece has checked the piece it found can be removed: notes it while it's
    /// still there.
    /// </summary>
    internal static void OnRemoveChecked(Piece piece)
    {
        if (_removing && !_replaying)
        {
            _pendingRemove = Describe(Kind.Remove, [piece]);
        }
    }

    /// <summary>
    /// Runs after Player.RemovePiece: if it removed the piece, that's a removal of its own.
    /// </summary>
    internal static void AfterRemovePiece(bool removed)
    {
        if (removed && _pendingRemove != null)
        {
            Push(_pendingRemove);
        }

        _removing = false;
        _pendingRemove = null;
    }

    /// <summary>
    /// A move made a new object for each piece; the history's handles follow them to it. Call before the
    /// originals are destroyed.
    /// </summary>
    internal static void Replaced(ZDOID oldId, ZDOID newId)
    {
        if (Handles.TryGetValue(oldId, out Handle handle))
        {
            _ = Handles.Remove(oldId);
            handle.Id = newId;
            handle.Destroyed = false;
            Handles[newId] = handle;
        }
    }

    /// <summary>
    /// Call after a move, with the moved pieces' new objects and where they were before.
    /// </summary>
    internal static void RecordMoved(List<GameObject> moved, List<(Vector3 Position, Quaternion Rotation)> from)
    {
        if (_replaying)
        {
            return;
        }

        List<Piece> pieces = [.. moved.Select(created => created != null ? created.GetComponent<Piece>() : null)];
        Entry entry = Describe(Kind.Move, pieces);
        if (entry == null || entry.Pieces.Count != from.Count)
        {
            return;
        }

        entry.MovedTo = entry.Poses;
        entry.Poses = from;
        Push(entry);
    }

    /// <summary>
    /// Runs when the game destroys an object, whoever did it.
    /// </summary>
    internal static void OnDestroyed(ZDOID id)
    {
        if (Handles.TryGetValue(id, out Handle handle))
        {
            handle.Destroyed = true;
        }
    }

    /// <summary>
    /// The entry for the pieces, or null if none of them can be built again by the hammer (a piece of
    /// several networked objects, a creature, an item, ...).
    /// </summary>
    private static Entry Describe(Kind kind, IEnumerable<Piece> pieces)
    {
        Entry entry = new() { Kind = kind, Pieces = [], Poses = [] };
        foreach (Piece piece in pieces)
        {
            ZNetView nview = piece != null ? piece.GetComponent<ZNetView>() : null;
            Piece prefab = nview != null && nview.IsValid() ? PrefabOf(piece) : null;
            if (prefab == null || !Rebuildable(piece))
            {
                if (kind == Kind.Move)
                {
                    return null;
                }

                continue;
            }

            ZDOID id = nview.GetZDO().m_uid;
            if (!Handles.TryGetValue(id, out Handle handle))
            {
                handle = new Handle { Id = id };
                Handles[id] = handle;
            }

            handle.Prefab = prefab;
            handle.Destroyed = false;
            entry.Pieces.Add(handle);
            entry.Poses.Add((piece.transform.position, piece.transform.rotation));
        }

        return entry.Pieces.Count > 0 ? entry : null;
    }

    private static Piece PrefabOf(Piece piece)
    {
        GameObject prefab = ZNetScene.instance.GetPrefab(Utils.GetPrefabName(piece.gameObject));
        return prefab != null ? prefab.GetComponent<Piece>() : null;
    }

    private static bool Rebuildable(Piece piece)
    {
        return piece.GetComponentsInChildren<ZNetView>(true).Length == 1
            && piece.GetComponentInChildren<Character>(true) == null
            && piece.GetComponentInChildren<ItemDrop>(true) == null
            && piece.GetComponentInChildren<TerrainModifier>(true) == null;
    }

    private static void Push(Entry entry)
    {
        Undos.Add(entry);
        Redos.Clear();
        int size = _size != null ? _size.Value : 50;
        while (Undos.Count > size)
        {
            Undos.RemoveAt(0);
        }
    }

    // ----- Undoing and redoing -----

    private static void Step(Player player, bool undo)
    {
        List<Entry> from = undo ? Undos : Redos;
        List<Entry> to = undo ? Redos : Undos;
        if (from.Count == 0)
        {
            player.Message(MessageHud.MessageType.TopLeft, undo ? "$betterhammer_undo_empty" : "$betterhammer_redo_empty");
            return;
        }

        Entry entry = from[from.Count - 1];
        Outcome outcome;
        _replaying = true;
        try
        {
            outcome = entry.Kind switch
            {
                Kind.Build => undo ? RemoveAll(player, entry) : BuildAll(player, entry),
                Kind.Remove => undo ? BuildAll(player, entry) : RemoveAll(player, entry),
                _ => MoveAll(player, entry, undo ? entry.Poses : entry.MovedTo),
            };
        }
        finally
        {
            _replaying = false;
            Placed.Clear();
        }

        if (outcome == Outcome.Blocked)
        {
            return;
        }

        from.RemoveAt(from.Count - 1);
        if (outcome == Outcome.Gone)
        {
            // Someone else already removed every piece, so there's nothing left to undo or redo.
            player.Message(MessageHud.MessageType.TopLeft, "$betterhammer_history_gone");
            return;
        }

        to.Add(entry);
        string token = (undo ? "$betterhammer_undid_" : "$betterhammer_redid_") + entry.Kind.ToString().ToLowerInvariant();
        player.Message(MessageHud.MessageType.TopLeft,
            Localization.instance.Localize(token, entry.Pieces.Count.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// The pieces that are still there, leaving out those destroyed since. Null (with a message) if any
    /// isn't loaded, as it's too far away.
    /// </summary>
    private static List<(Handle Handle, Piece Piece, int Index)> Find(Player player, Entry entry)
    {
        List<(Handle, Piece, int)> found = [];
        for (int i = 0; i < entry.Pieces.Count; i++)
        {
            Handle handle = entry.Pieces[i];
            if (handle.Destroyed)
            {
                continue;
            }

            GameObject instance = ZNetScene.instance.FindInstance(handle.Id);
            Piece piece = instance != null ? instance.GetComponent<Piece>() : null;
            if (piece == null)
            {
                player.Message(MessageHud.MessageType.Center, "$betterhammer_history_far");
                return null;
            }

            found.Add((handle, piece, i));
        }

        return found;
    }

    private static Outcome RemoveAll(Player player, Entry entry)
    {
        List<(Handle Handle, Piece Piece, int Index)> found = Find(player, entry);
        if (found == null)
        {
            return Outcome.Blocked;
        }

        if (found.Count == 0)
        {
            return Outcome.Gone;
        }

        if (found.Any(item => !MultiCopy.CanRemove(player, item.Piece)))
        {
            player.Message(MessageHud.MessageType.Center, "$betterhammer_history_blocked");
            return Outcome.Blocked;
        }

        foreach ((Handle _, Piece piece, int _) in found)
        {
            MultiCopy.Remove(player, piece);
        }

        Swing(player);
        return Outcome.Done;
    }

    private static Outcome BuildAll(Player player, Entry entry)
    {
        List<Piece> prefabs = [.. entry.Pieces.Select(handle => handle.Prefab)];
        if (prefabs.Any(prefab => prefab == null))
        {
            return Outcome.Gone;
        }

        foreach ((Vector3 position, Quaternion _) in entry.Poses)
        {
            if (Location.IsInsideNoBuildLocation(position))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_nobuildzone");
                return Outcome.Blocked;
            }

            if (!PrivateArea.CheckAccess(position))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_privatezone");
                return Outcome.Blocked;
            }
        }

        if (!MultiCopy.HaveStations(player, prefabs))
        {
            player.Message(MessageHud.MessageType.Center, "$msg_missingstation");
            return Outcome.Blocked;
        }

        bool free = player.m_noPlacementCost || ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildCost);
        Piece cost = CostOf(prefabs);
        if (!player.m_noPlacementCost && !player.HaveRequirements(cost, Player.RequirementMode.CanBuild))
        {
            player.Message(MessageHud.MessageType.Center, "$msg_missingrequirement");
            return Outcome.Blocked;
        }

        // Placing a piece that starts at a random turn re-rolls the build rotation.
        int rotation = player.m_placeRotation;
        for (int i = 0; i < prefabs.Count; i++)
        {
            Placed.Clear();
            player.PlacePiece(prefabs[i], entry.Poses[i].Position, entry.Poses[i].Rotation, doAttack: i == 0);
            ZNetView nview = Placed.Count > 0 && Placed[0] != null ? Placed[0].GetComponent<ZNetView>() : null;
            if (nview != null && nview.IsValid())
            {
                Handle handle = entry.Pieces[i];
                _ = Handles.Remove(handle.Id);
                handle.Id = nview.GetZDO().m_uid;
                handle.Destroyed = false;
                Handles[handle.Id] = handle;
            }
        }

        player.m_placeRotation = rotation;
        if (!free)
        {
            player.ConsumeResources(cost.m_resources, 0);
        }

        return Outcome.Done;
    }

    private static Outcome MoveAll(Player player, Entry entry, List<(Vector3 Position, Quaternion Rotation)> poses)
    {
        List<(Handle Handle, Piece Piece, int Index)> found = Find(player, entry);
        if (found == null)
        {
            return Outcome.Blocked;
        }

        if (found.Count == 0)
        {
            return Outcome.Gone;
        }

        foreach ((Handle _, Piece piece, int _) in found)
        {
            if (!PieceMover.CanMove(player, piece, out _))
            {
                return Outcome.Blocked; // CanMove says why
            }
        }

        foreach ((Vector3 position, Quaternion _) in found.Select(item => poses[item.Index]))
        {
            if (Location.IsInsideNoBuildLocation(position))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_nobuildzone");
                return Outcome.Blocked;
            }

            if (!PrivateArea.CheckAccess(position))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_privatezone");
                return Outcome.Blocked;
            }
        }

        List<ZNetView> sources = [.. found.Select(item => item.Piece.GetComponent<ZNetView>())];
        List<(Vector3, Quaternion)> targets = [.. found.Select(item => poses[item.Index])];
        if (PieceMover.Relocate(player, sources, targets) == null)
        {
            player.Message(MessageHud.MessageType.Center, "$betterhammer_move_failed");
            return Outcome.Blocked;
        }

        Swing(player);
        return Outcome.Done;
    }

    private static Piece CostOf(List<Piece> prefabs)
    {
        if (_costHolder == null)
        {
            GameObject holder = new("BetterHammer_UndoCost");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            _costHolder = holder.AddComponent<Piece>();
        }

        _costHolder.m_resources = [.. CopyLibrary.CombineRequirements(prefabs, out _)];
        return _costHolder;
    }

    private static void Swing(Player player)
    {
        ItemDrop.ItemData tool = player.GetRightItem();
        if (tool != null)
        {
            player.FaceLookDirection();
            player.m_zanim.SetTrigger(tool.m_shared.m_attack.m_attackAnimation);
        }
    }
}
