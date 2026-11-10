using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using BepInEx;
using BepInEx.Configuration;

using Jotunn.Managers;

using Newtonsoft.Json;

using TMPro;

using UnityEngine;
using UnityEngine.EventSystems;

using Object = UnityEngine.Object;

namespace BetterHammer;

/// <summary>
/// <para>
/// Saved copies: each confirmed selection is stored in BepInEx/config/BetterHammer/copies.json and
/// becomes an entry in the hammer's "Copies" build menu tab, named "Copy 1", "Copy 2" and so on, with a
/// rendered picture of the whole copy as its icon. The file is shared by every character and world on
/// this computer.
/// </para>
/// <para>
/// A menu entry is a stand-in piece that's never built itself: selecting it hands over to
/// <see cref="MultiCopy"/>, which shows and builds the real pieces. Hovering an entry in the build menu
/// and pressing Delete removes it after a confirmation.
/// </para>
/// </summary>
internal static class CopyLibrary
{
    private const string CategoryName = "Copies";

    private static string FilePath => Path.Combine(Paths.ConfigPath, "BetterHammer", "copies.json");

    /// <summary>
    /// Written with Newtonsoft.Json (which ships with the game). Unity's JsonUtility silently left the
    /// list of copies out of the file. Vectors are plain number arrays, since Newtonsoft can't write
    /// Unity's Vector3 and Quaternion without help.
    /// </summary>
    private sealed class LibraryFile
    {
        public int NextNumber = 1;
        public List<SavedCopy> Copies = [];
    }

    internal sealed class SavedCopy
    {
        public int Number;
        public List<SavedPiece> Pieces = [];
    }

    /// <summary>
    /// A piece of a copy: its prefab, its position relative to the first piece, and its rotation.
    /// </summary>
    internal sealed class SavedPiece
    {
        public string Prefab;

        /// <summary>x, y, z</summary>
        public float[] Position;

        /// <summary>x, y, z, w</summary>
        public float[] Rotation;
    }

    internal sealed class Part
    {
        public Piece Prefab;
        public Vector3 Position;
        public Quaternion Rotation;
        public int SnapCount;
    }

    internal sealed class Copy
    {
        public string Name;

        /// <summary>
        /// The build menu entry. Its requirements are the combined requirements of every part.
        /// </summary>
        public Piece Piece;
        public readonly List<Part> Parts = [];
        public readonly List<Piece> Prefabs = [];
        public List<Piece> StationPieces = [];
        public string Description;
        public SavedCopy Saved;
    }

    private static readonly List<Copy> Copies = [];
    private static readonly HashSet<string> Names = [];
    private static readonly List<Transform> SnapPoints = [];
    private static LibraryFile _file;
    private static PieceTable _table;
    private static GameObject _container;
    private static Piece.PieceCategory _category;
    private static ConfigEntry<KeyCode> _deleteKey;

    internal static KeyCode DeleteKey => _deleteKey != null ? _deleteKey.Value : KeyCode.Delete;

    public static void BindConfig(ConfigFile config)
    {
        _deleteKey = config.Bind(
            "Keybinds",
            "Delete",
            KeyCode.Delete,
            "With the build menu open, hover a saved copy in the Copies tab and press this to delete it. " +
            "With the build menu closed, press it to remove every piece selected for copying.");
    }

    /// <summary>
    /// Loads the saved copies into the hammer's piece table the first time a world is entered. The table
    /// and the prefabs outlive the world, so this happens once per game session.
    /// </summary>
    internal static void EnsureLoaded(Player player, PieceTable table)
    {
        if (_file != null || table == null || ZNetScene.instance == null)
        {
            return;
        }

        _table = table;
        _category = PieceManager.Instance.AddPieceCategory(CategoryName);
        _container = new GameObject("BetterHammer_Copies");
        _container.SetActive(false);
        Object.DontDestroyOnLoad(_container);

        _file = Read();
        foreach (SavedCopy saved in _file.Copies)
        {
            _ = Register(saved);
        }

        player.UpdateAvailablePiecesList();
    }

    internal static Copy Find(Piece piece)
    {
        if (piece == null)
        {
            return null;
        }

        foreach (Copy copy in Copies)
        {
            if (copy.Piece == piece)
            {
                return copy;
            }
        }

        return null;
    }

    internal static bool IsCopyName(string name)
    {
        return Names.Contains(name);
    }

    internal static IEnumerable<string> AllNames => Names;

    /// <summary>
    /// Saves a new copy of the given pieces (prefab, world position, world rotation) and adds it to the
    /// Copies tab.
    /// </summary>
    internal static Copy Add(Player player, IList<(Piece Prefab, Vector3 Position, Quaternion Rotation)> pieces)
    {
        if (_file == null || pieces.Count == 0)
        {
            return null;
        }

        Vector3 origin = pieces[0].Position;
        SavedCopy saved = new() { Number = _file.NextNumber++ };
        foreach ((Piece prefab, Vector3 position, Quaternion rotation) in pieces)
        {
            Vector3 offset = position - origin;
            saved.Pieces.Add(new SavedPiece
            {
                Prefab = prefab.name,
                Position = [offset.x, offset.y, offset.z],
                Rotation = [rotation.x, rotation.y, rotation.z, rotation.w],
            });
        }

        _file.Copies.Add(saved);
        Save();
        Copy copy = Register(saved);
        player.UpdateAvailablePiecesList();
        return copy;
    }

    /// <summary>
    /// Delete key over a copy in the build menu: asks, then deletes it.
    /// </summary>
    internal static void UpdateDeleteInput(Player player)
    {
        if (_deleteKey == null
            || !Hud.IsPieceSelectionVisible()
            || UnifiedPopup.IsVisible()
            || !Input.GetKeyDown(_deleteKey.Value)
            || TypingInTextField())
        {
            return;
        }

        Copy copy = Find(Hud.instance.m_hoveredPiece);
        if (copy == null)
        {
            return;
        }

        UnifiedPopup.Push(new YesNoPopup(
            "$betterhammer_copy_delete_title",
            Localization.instance.Localize("$betterhammer_copy_delete_text", copy.Name),
            () =>
            {
                UnifiedPopup.Pop();
                Delete(player, copy);
            },
            UnifiedPopup.Pop));
    }

    /// <summary>
    /// Delete and Backspace also edit text, so they're left alone while typing in the build menu's search
    /// box, the console or a sign.
    /// </summary>
    internal static bool TypingInTextField()
    {
        if (Console.IsVisible() || TextInput.IsVisible())
        {
            return true;
        }

        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        TMP_InputField field = selected != null ? selected.GetComponent<TMP_InputField>() : null;
        return field != null && field.isFocused;
    }

    internal static string DeleteHint()
    {
        return _deleteKey != null ? Localization.instance.Localize("$betterhammer_copy_delete_hint", _deleteKey.Value.ToString()) : "";
    }

    private static void Delete(Player player, Copy copy)
    {
        // The stand-in object is kept (just out of the table), in case the game still refers to it.
        _ = _table.m_pieces.Remove(copy.Piece.gameObject);
        _ = Copies.Remove(copy);
        _ = Names.Remove(copy.Name);
        _ = _file.Copies.Remove(copy.Saved);
        Save();

        if (player != null)
        {
            player.UpdateAvailablePiecesList();
            if (Hud.IsPieceSelectionVisible())
            {
                Hud.instance.m_buildUi.OpenBuildMenu();
            }

            player.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$betterhammer_copy_deleted", copy.Name));
        }
    }

    /// <summary>
    /// A part for the given prefab at the given place, with its snap points counted.
    /// </summary>
    internal static Part MakePart(Piece prefab, Vector3 position, Quaternion rotation)
    {
        SnapPoints.Clear();
        prefab.GetSnapPoints(SnapPoints);
        int snapCount = SnapPoints.Count;
        SnapPoints.Clear();
        return new Part { Prefab = prefab, Position = position, Rotation = rotation, SnapCount = snapCount };
    }

    private static Copy Register(SavedCopy saved)
    {
        Copy copy = new()
        {
            Name = Localization.instance.Localize("$betterhammer_copy_name", saved.Number.ToString(CultureInfo.InvariantCulture)),
            Saved = saved,
        };

        foreach (SavedPiece piece in saved.Pieces)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(piece.Prefab);
            Piece prefabPiece = prefab != null ? prefab.GetComponent<Piece>() : null;
            if (prefabPiece == null)
            {
                // A piece from a mod that's no longer installed. The copy stays in the file.
                Debug.LogWarning("BetterHammer: copy " + saved.Number + " uses missing piece " + piece.Prefab + ", not adding it.");
                return null;
            }

            SnapPoints.Clear();
            prefabPiece.GetSnapPoints(SnapPoints);
            copy.Parts.Add(new Part
            {
                Prefab = prefabPiece,
                Position = new Vector3(piece.Position[0], piece.Position[1], piece.Position[2]),
                Rotation = new Quaternion(piece.Rotation[0], piece.Rotation[1], piece.Rotation[2], piece.Rotation[3]).normalized,
                SnapCount = SnapPoints.Count,
            });
            copy.Prefabs.Add(prefabPiece);
        }

        SnapPoints.Clear();
        if (copy.Parts.Count == 0)
        {
            return null;
        }

        GameObject holder = new("BetterHammer_Copy_" + saved.Number.ToString(CultureInfo.InvariantCulture));
        holder.transform.SetParent(_container.transform, false);
        Piece entry = holder.AddComponent<Piece>();
        entry.m_name = copy.Name;
        entry.m_category = _category;
        entry.m_resources = [.. CombineRequirements(copy.Prefabs, out copy.StationPieces)];
        entry.m_craftingStation = copy.StationPieces.Count > 0 ? copy.StationPieces[0].m_craftingStation : null;
        copy.Description = DescribePieces(copy.Prefabs);
        entry.m_description = copy.Description;
        entry.m_icon = RenderIcon(copy) ?? copy.Prefabs[0].m_icon;
        copy.Piece = entry;

        _table.m_pieces.Add(holder);
        Copies.Add(copy);
        _ = Names.Add(copy.Name);
        return copy;
    }

    /// <summary>
    /// Every material of the pieces added up, in order of first appearance, and one piece per crafting
    /// station they need.
    /// </summary>
    internal static List<Piece.Requirement> CombineRequirements(IEnumerable<Piece> pieces, out List<Piece> stationPieces)
    {
        List<Piece.Requirement> combined = [];
        Dictionary<string, Piece.Requirement> byItem = [];
        HashSet<string> stations = [];
        stationPieces = [];
        foreach (Piece piece in pieces)
        {
            foreach (Piece.Requirement requirement in piece.m_resources)
            {
                if (requirement.m_resItem == null || requirement.m_amount <= 0)
                {
                    continue;
                }

                string item = requirement.m_resItem.m_itemData.m_shared.m_name;
                if (byItem.TryGetValue(item, out Piece.Requirement total))
                {
                    total.m_amount += requirement.m_amount;
                    continue;
                }

                total = new Piece.Requirement
                {
                    m_resItem = requirement.m_resItem,
                    m_amount = requirement.m_amount,
                    m_recover = requirement.m_recover,
                };
                byItem[item] = total;
                combined.Add(total);
            }

            if (piece.m_craftingStation != null && stations.Add(piece.m_craftingStation.m_name))
            {
                stationPieces.Add(piece);
            }
        }

        return combined;
    }

    /// <summary>
    /// "3× Wood wall, 1× Wood door".
    /// </summary>
    internal static string DescribePieces(IEnumerable<Piece> pieces)
    {
        return string.Join(", ", pieces
            .GroupBy(piece => piece.m_name)
            .Select(same => same.Count().ToString(CultureInfo.InvariantCulture) + "× " + Localization.instance.Localize(same.Key)));
    }

    /// <summary>
    /// A picture of the whole copy. The pieces are put together out of sight with their networking
    /// switched off (the same way the game makes placement ghosts), rendered, and thrown away.
    /// </summary>
    private static Sprite RenderIcon(Copy copy)
    {
        GameObject root = new("BetterHammer_CopyPreview");
        root.transform.position = new Vector3(0f, -5000f, 0f);
        ZNetView.m_forceDisableInit = true;
        TerrainOp.m_forceDisableTerrainOps = true;
        try
        {
            foreach (Part part in copy.Parts)
            {
                _ = Object.Instantiate(part.Prefab.gameObject, root.transform.position + part.Position, part.Rotation, root.transform);
            }
        }
        finally
        {
            ZNetView.m_forceDisableInit = false;
            TerrainOp.m_forceDisableTerrainOps = false;
        }

        try
        {
            return RenderManager.Instance.Render(new RenderManager.RenderRequest(root)
            {
                Rotation = RenderManager.IsometricRotation,
                Width = 128,
                Height = 128,
            });
        }
        catch (Exception e)
        {
            Debug.LogWarning("BetterHammer: couldn't render an icon for " + copy.Name + ": " + e.Message);
            return null;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static LibraryFile Read()
    {
        LibraryFile file = null;
        string path = FilePath;
        if (File.Exists(path))
        {
            try
            {
                file = JsonConvert.DeserializeObject<LibraryFile>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                // Keep the unreadable file rather than overwriting it with an empty library.
                string backup = path + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                Debug.LogError("BetterHammer: couldn't read " + path + " (" + e.Message + "), moved it to " + backup);
                try
                {
                    File.Move(path, backup);
                }
                catch (Exception moveError)
                {
                    Debug.LogError("BetterHammer: couldn't move it either: " + moveError.Message);
                }
            }
        }

        file ??= new LibraryFile();
        file.Copies ??= [];
        foreach (SavedCopy copy in file.Copies)
        {
            _ = copy?.Pieces?.RemoveAll(piece => piece?.Prefab == null || piece.Position is not { Length: 3 } || piece.Rotation is not { Length: 4 });
        }

        _ = file.Copies.RemoveAll(copy => copy?.Pieces == null || copy.Pieces.Count == 0);
        int highest = file.Copies.Count > 0 ? file.Copies.Max(copy => copy.Number) : 0;
        file.NextNumber = Mathf.Max(file.NextNumber, highest + 1);
        return file;
    }

    private static void Save()
    {
        try
        {
            string path = FilePath;
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(_file, Formatting.Indented));
        }
        catch (Exception e)
        {
            Debug.LogError("BetterHammer: couldn't save copies to " + FilePath + ": " + e.Message);
        }
    }
}
