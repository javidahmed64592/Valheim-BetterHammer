using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using BepInEx;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using UnityEngine;

namespace BetterHammer.BuildMenu;

/// <summary>
/// What the build menu remembers between sessions, for all characters: which sections are collapsed,
/// how each tab groups and sorts its pieces, and the order of pieces in each favourites category.
/// Saved in <c>BepInEx/config/BetterHammer/build_menu.json</c>.
/// </summary>
internal static class MenuState
{
    private sealed class StateFile
    {
        public List<string> Collapsed { get; set; } = [];

        /// <summary>
        /// Tab (its piece list's type name) to how it groups and sorts.
        /// </summary>
        public Dictionary<string, ViewState> Views { get; set; } = [];

        /// <summary>
        /// Favourites category name ("" for uncategorised) to its pieces' prefab names, in the order the
        /// player arranged them.
        /// </summary>
        public Dictionary<string, List<string>> FavoriteOrder { get; set; } = [];
    }

    private sealed class ViewState
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public GroupMode Group { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public SortMode Sort { get; set; }
    }

    private static StateFile _file;
    private static HashSet<string> _collapsed;

    private static string FilePath => Path.Combine(Paths.ConfigPath, "BetterHammer", "build_menu.json");

    internal static bool IsCollapsed(string key)
    {
        EnsureLoaded();
        return _collapsed.Contains(key);
    }

    internal static void SetCollapsed(string key, bool collapsed)
    {
        EnsureLoaded();
        if (collapsed ? _collapsed.Add(key) : _collapsed.Remove(key))
        {
            _file.Collapsed = [.. _collapsed];
            _file.Collapsed.Sort(StringComparer.Ordinal);
            Save();
        }
    }

    internal static GroupMode GroupOf(string tab)
    {
        EnsureLoaded();
        return _file.Views.TryGetValue(tab, out ViewState view) && view != null ? view.Group : GroupMode.Type;
    }

    /// <summary>
    /// How the tab sorts; Default if it would sort by what it's grouped by.
    /// </summary>
    internal static SortMode SortOf(string tab)
    {
        EnsureLoaded();
        return _file.Views.TryGetValue(tab, out ViewState view) && view != null && MenuView.Allowed(view.Group, view.Sort)
            ? view.Sort
            : SortMode.Default;
    }

    internal static void SetView(string tab, GroupMode group, SortMode sort)
    {
        EnsureLoaded();
        _file.Views[tab] = new ViewState { Group = group, Sort = sort };
        Save();
    }

    /// <summary>
    /// The order the player arranged a favourites category's pieces in, as prefab names; empty if never.
    /// </summary>
    internal static IReadOnlyList<string> FavoriteOrderOf(string category)
    {
        EnsureLoaded();
        return _file.FavoriteOrder.TryGetValue(category, out List<string> order) && order != null ? order : [];
    }

    internal static void SetFavoriteOrder(string category, List<string> order)
    {
        EnsureLoaded();
        _file.FavoriteOrder[category] = order;
        Save();
    }

    /// <summary>
    /// Keeps a category's arranged order when it's renamed.
    /// </summary>
    internal static void RenameFavoriteOrder(string from, string to)
    {
        EnsureLoaded();
        if (_file.FavoriteOrder.TryGetValue(from, out List<string> order))
        {
            _ = _file.FavoriteOrder.Remove(from);
            _file.FavoriteOrder[to] = order;
            Save();
        }
    }

    private static void EnsureLoaded()
    {
        if (_file != null)
        {
            return;
        }

        string path = FilePath;
        if (File.Exists(path))
        {
            try
            {
                _file = JsonConvert.DeserializeObject<StateFile>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                // Keep the unreadable file rather than overwriting it.
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

        _file ??= new StateFile();
        _file.Collapsed ??= [];
        _file.Views ??= [];
        _file.FavoriteOrder ??= [];
        _collapsed = [.. _file.Collapsed];
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
            Debug.LogError("BetterHammer: couldn't save the build menu state to " + FilePath + ": " + e.Message);
        }
    }
}
