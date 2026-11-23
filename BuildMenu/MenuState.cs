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
/// and how each tab groups and sorts its pieces.
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

    internal static SortMode SortOf(string tab)
    {
        EnsureLoaded();
        return _file.Views.TryGetValue(tab, out ViewState view) && view != null ? view.Sort : SortMode.Default;
    }

    internal static void SetView(string tab, GroupMode group, SortMode sort)
    {
        EnsureLoaded();
        _file.Views[tab] = new ViewState { Group = group, Sort = sort };
        Save();
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
