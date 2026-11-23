using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

using BepInEx;

using Newtonsoft.Json;

using UnityEngine;

namespace BetterHammer.BuildMenu;

/// <summary>
/// The rules that sort pieces in the build menu: which category a piece is moved to when its tags put it
/// in the wrong one, and the sections inside each category, matched on the piece's prefab name. The
/// defaults are built in. <c>BepInEx/config/BetterHammer/build_menu_rules.json</c> can move more pieces
/// and replace a category's sections; a copy of the defaults is written next to it for reference.
/// </summary>
internal static class SectionRules
{
    private const string DefaultsResource = "BetterHammer.BuildMenu.DefaultRules.json";

    /// <summary>
    /// The layout of both the built-in rules and the player's file.
    /// </summary>
    private sealed class RulesFile
    {
        /// <summary>
        /// Prefab name to the category (a usage tag name such as "Wall") the piece goes under.
        /// </summary>
        public Dictionary<string, string> Moves { get; set; } = [];

        /// <summary>
        /// Category (a usage tag name) to its sections, in the order they're shown.
        /// </summary>
        public Dictionary<string, List<SectionRule>> Sections { get; set; } = [];
    }

    private sealed class SectionRule
    {
        /// <summary>
        /// The header, as text or a $translation token.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A regular expression on the prefab name, ignoring case.
        /// </summary>
        public string Match { get; set; }

        /// <summary>
        /// Takes the pieces no other section of the category matched.
        /// </summary>
        public bool Fallback { get; set; }
    }

    internal sealed class Section
    {
        internal Section(string name, Regex match, bool fallback, int position)
        {
            Name = name;
            Match = match;
            Fallback = fallback;
            Position = position;
        }

        internal string Name { get; }

        internal Regex Match { get; }

        internal bool Fallback { get; }

        /// <summary>
        /// Where the section is shown among the category's sections.
        /// </summary>
        internal int Position { get; }
    }

    private static Dictionary<string, Piece.UsageTagFlags> _moves;
    private static Dictionary<Piece.UsageTagFlags, List<Section>> _sections;

    private static string FilePath => Path.Combine(Paths.ConfigPath, "BetterHammer", "build_menu_rules.json");

    private static string DefaultsPath => Path.Combine(Paths.ConfigPath, "BetterHammer", "build_menu_rules.defaults.json");

    /// <summary>
    /// The category a piece is moved to, if a rule moves it.
    /// </summary>
    internal static bool TryGetMove(string prefabName, out Piece.UsageTagFlags category)
    {
        EnsureLoaded();
        return _moves.TryGetValue(prefabName, out category);
    }

    /// <summary>
    /// The section of a category a piece goes under, or null when the category has no sections or none
    /// matches.
    /// </summary>
    internal static Section SectionOf(Piece.UsageTagFlags category, string prefabName)
    {
        EnsureLoaded();
        if (!_sections.TryGetValue(category, out List<Section> sections))
        {
            return null;
        }

        Section fallback = null;
        foreach (Section section in sections)
        {
            if (section.Fallback)
            {
                fallback ??= section;
            }
            else if (section.Match != null && section.Match.IsMatch(prefabName))
            {
                return section;
            }
        }

        return fallback;
    }

    /// <summary>
    /// Whether the category is split into sections at all.
    /// </summary>
    internal static bool HasSections(Piece.UsageTagFlags category)
    {
        EnsureLoaded();
        return _sections.TryGetValue(category, out List<Section> sections) && sections.Count > 0;
    }

    private static void EnsureLoaded()
    {
        if (_moves != null)
        {
            return;
        }

        _moves = [];
        _sections = [];

        string defaults = ReadDefaults();
        Apply(Parse(defaults, "the built-in rules"));
        WriteReferenceFiles(defaults);
        if (File.Exists(FilePath))
        {
            try
            {
                Apply(Parse(File.ReadAllText(FilePath), FilePath));
            }
            catch (Exception e)
            {
                Debug.LogError("BetterHammer: couldn't read " + FilePath + ": " + e.Message);
            }
        }
    }

    private static string ReadDefaults()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(DefaultsResource);
        if (stream == null)
        {
            Debug.LogError("BetterHammer: the built-in build menu rules are missing");
            return "{}";
        }

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static RulesFile Parse(string json, string source)
    {
        try
        {
            return JsonConvert.DeserializeObject<RulesFile>(json) ?? new RulesFile();
        }
        catch (Exception e)
        {
            Debug.LogError("BetterHammer: couldn't read the build menu rules in " + source + ": " + e.Message);
            return new RulesFile();
        }
    }

    /// <summary>
    /// Adds a file's rules: its moves are added to (or replace) the ones so far, and each category it
    /// lists sections for replaces that category's sections.
    /// </summary>
    private static void Apply(RulesFile file)
    {
        if (file.Moves != null)
        {
            foreach (KeyValuePair<string, string> move in file.Moves)
            {
                if (TryParseCategory(move.Value, out Piece.UsageTagFlags category))
                {
                    _moves[move.Key] = category;
                }
            }
        }

        if (file.Sections == null)
        {
            return;
        }

        foreach (KeyValuePair<string, List<SectionRule>> entry in file.Sections)
        {
            if (!TryParseCategory(entry.Key, out Piece.UsageTagFlags category))
            {
                continue;
            }

            List<Section> sections = [];
            foreach (SectionRule rule in entry.Value ?? [])
            {
                if (rule == null || string.IsNullOrEmpty(rule.Name))
                {
                    continue;
                }

                Regex match = null;
                if (!string.IsNullOrEmpty(rule.Match))
                {
                    try
                    {
                        match = new Regex(rule.Match, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    }
                    catch (ArgumentException e)
                    {
                        Debug.LogError("BetterHammer: the build menu section " + rule.Name + " has a broken Match: " + e.Message);
                        continue;
                    }
                }

                if (match != null || rule.Fallback)
                {
                    sections.Add(new Section(rule.Name, match, rule.Fallback, sections.Count));
                }
            }

            _sections[category] = sections;
        }
    }

    private static bool TryParseCategory(string name, out Piece.UsageTagFlags category)
    {
        if (!string.IsNullOrEmpty(name) && Enum.TryParse(name, true, out category) && Enum.IsDefined(typeof(Piece.UsageTagFlags), category))
        {
            return true;
        }

        Debug.LogWarning("BetterHammer: unknown build menu category '" + name + "' in the rules. Use one of: "
            + string.Join(", ", Enum.GetNames(typeof(Piece.UsageTagFlags))));
        category = 0;
        return false;
    }

    /// <summary>
    /// Keeps a copy of the built-in rules next to the player's file, and starts the player's file empty.
    /// </summary>
    private static void WriteReferenceFiles(string defaults)
    {
        try
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(DefaultsPath));
            File.WriteAllText(DefaultsPath, defaults);
            if (!File.Exists(FilePath))
            {
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(new RulesFile(), Formatting.Indented));
            }
        }
        catch (Exception e)
        {
            Debug.LogError("BetterHammer: couldn't write the build menu rules files: " + e.Message);
        }
    }
}
