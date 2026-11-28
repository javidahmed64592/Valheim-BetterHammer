using System.Collections.Generic;

using BepInEx;
using BepInEx.Configuration;

using HarmonyLib;

using UnityEngine;

namespace BetterHammer.Core;

/// <summary>
/// The key held with a keybind's key, chosen from a dropdown. Either side of the keyboard counts.
/// </summary>
internal enum KeyModifier
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 3,
}

/// <summary>
/// A keybind set in the config with two dropdowns, so it can't be mistyped: a key, and a modifier held with it (or
/// None for the key on its own). The key goes down while the modifier is held; other keys held at the same time, such
/// as movement keys or Shift to run, don't stop it. A key on its own doesn't count while one of this mod's
/// combos with the same key is held, so Alt+H doesn't also do what H does.
/// </summary>
internal sealed class KeyBinding
{
    private static readonly List<KeyBinding> All = [];

    private readonly ConfigEntry<KeyModifier> _modifier;

    private KeyBinding(ConfigEntry<KeyCode> key, ConfigEntry<KeyModifier> modifier)
    {
        KeyEntry = key;
        _modifier = modifier;
    }

    /// <summary>The key's own setting, for code that reads it through a game button (a Jotunn button).</summary>
    internal ConfigEntry<KeyCode> KeyEntry { get; }

    internal KeyCode Key => KeyEntry.Value;

    internal KeyModifier Modifier => _modifier.Value;

    /// <summary>
    /// Binds the key as <paramref name="name"/> and its modifier as "<paramref name="name"/> Modifier". A value
    /// saved by an older version as a BepInEx shortcut ("Z + LeftControl") is split between the two.
    /// </summary>
    internal static KeyBinding Bind(ConfigFile config, string section, string name, KeyCode key, KeyModifier modifier, string description)
    {
        string modifierName = name + " Modifier";
        MoveOldShortcut(config, new ConfigDefinition(section, name), new ConfigDefinition(section, modifierName));

        KeyBinding binding = new(
            config.Bind(section, name, key, description),
            config.Bind(section, modifierName, modifier, $"Held with the {name} key: Shift, Ctrl or Alt (either side), or None for the key on its own."));
        All.Add(binding);
        return binding;
    }

    /// <summary>True on the frame the key goes down while the modifier is held.</summary>
    internal bool IsDown()
    {
        return Key != KeyCode.None && UnityInput.Current.GetKeyDown(Key) && Accepts();
    }

    /// <summary>True while the key and the modifier are held.</summary>
    internal bool IsHeld()
    {
        return Key != KeyCode.None && UnityInput.Current.GetKey(Key) && Accepts();
    }

    /// <summary>
    /// Whether a press of the key counts: the modifier is held, and a key on its own isn't one of this mod's combos.
    /// For a key read through a game button, call this when the button goes down.
    /// </summary>
    internal bool Accepts()
    {
        if (!ModifierHeld(Modifier))
        {
            return false;
        }

        if (Modifier != KeyModifier.None)
        {
            return true;
        }

        foreach (KeyBinding other in All)
        {
            if (other != this && other.Modifier != KeyModifier.None && other.Key == Key && ModifierHeld(other.Modifier))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when one of this mod's combos (a key with a modifier) went down this frame.</summary>
    internal static bool AnyComboDown()
    {
        foreach (KeyBinding binding in All)
        {
            if (binding.Modifier != KeyModifier.None && binding.IsDown())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The keys as the key hints show them, modifier first, named like the game's own hints.</summary>
    internal string[] HintKeys()
    {
        string key = KeyName(Key);
        return Modifier switch
        {
            KeyModifier.Shift => ["Left Shift", key],
            KeyModifier.Ctrl => ["Left Ctrl", key],
            KeyModifier.Alt => ["Left Alt", key],
            _ => [key],
        };
    }

    public override string ToString()
    {
        string key = KeyName(Key);
        return Modifier == KeyModifier.None ? key : Modifier + " + " + key;
    }

    private static string KeyName(KeyCode key)
    {
        return key switch
        {
            KeyCode.LeftControl => "Left Ctrl",
            KeyCode.RightControl => "Right Ctrl",
            KeyCode.LeftShift => "Left Shift",
            KeyCode.RightShift => "Right Shift",
            KeyCode.LeftAlt => "Left Alt",
            KeyCode.RightAlt => "Right Alt",
            KeyCode.Mouse0 => "Mouse 1",
            KeyCode.Mouse1 => "Mouse 2",
            KeyCode.Mouse2 => "Mouse 3",
            _ => key.ToString(),
        };
    }

    private static bool ModifierHeld(KeyModifier modifier)
    {
        return modifier switch
        {
            KeyModifier.Shift => UnityInput.Current.GetKey(KeyCode.LeftShift) || UnityInput.Current.GetKey(KeyCode.RightShift),
            KeyModifier.Ctrl => UnityInput.Current.GetKey(KeyCode.LeftControl) || UnityInput.Current.GetKey(KeyCode.RightControl),
            KeyModifier.Alt => UnityInput.Current.GetKey(KeyCode.LeftAlt) || UnityInput.Current.GetKey(KeyCode.RightAlt),
            _ => true,
        };
    }

    /// <summary>
    /// A setting that was a BepInEx shortcut is still in the file as text such as "Z + LeftControl", which the key's
    /// dropdown can't read. Before binding, the text is split into the key and the modifier, so the player's choice
    /// carries over.
    /// </summary>
    private static void MoveOldShortcut(ConfigFile config, ConfigDefinition keyDefinition, ConfigDefinition modifierDefinition)
    {
        if (AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(config) is not Dictionary<ConfigDefinition, string> saved
            || !saved.TryGetValue(keyDefinition, out string text)
            || System.Enum.TryParse(text, out KeyCode _))
        {
            return;
        }

        KeyboardShortcut shortcut = KeyboardShortcut.Deserialize(text);
        saved[keyDefinition] = shortcut.MainKey.ToString();

        KeyModifier modifier = KeyModifier.None;
        foreach (KeyCode held in shortcut.Modifiers)
        {
            modifier = held switch
            {
                KeyCode.LeftShift or KeyCode.RightShift => KeyModifier.Shift,
                KeyCode.LeftControl or KeyCode.RightControl => KeyModifier.Ctrl,
                KeyCode.LeftAlt or KeyCode.RightAlt => KeyModifier.Alt,
                _ => modifier,
            };
        }

        if (!saved.ContainsKey(modifierDefinition))
        {
            saved[modifierDefinition] = modifier.ToString();
        }
    }
}
