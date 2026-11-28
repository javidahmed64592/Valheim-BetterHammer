using BepInEx.Configuration;

using BetterHammer.Core;
using BetterHammer.Structures;

using UnityEngine;

namespace BetterHammer.BuildMenu;

/// <summary>
/// Pieces the player hid from the build menu: hover one and press the Hide key. Hidden pieces only show in
/// the Categories tab's Hidden category, where the same key brings them back.
/// </summary>
internal static class HiddenPieces
{
    private static KeyBinding _key;

    internal static void BindConfig(ConfigFile config)
    {
        _key = KeyBinding.Bind(
            config,
            "Keybinds",
            "Hide Piece",
            KeyCode.H,
            KeyModifier.None,
            "With the build menu open, hover a piece and press this to hide it from the menu. Hidden pieces are " +
            "listed in the Hidden category, where pressing it again brings them back.");
    }

    internal static bool IsHidden(Piece piece)
    {
        return piece != null && MenuState.IsHidden(piece.gameObject.name);
    }

    internal static void UpdateMenuInput(Player player)
    {
        BuildUi ui = Hud.instance != null ? Hud.instance.m_buildUi : null;
        if (_key == null
            || ui == null
            || SectionedGrid.For(ui) == null
            || !Hud.IsPieceSelectionVisible()
            || UnifiedPopup.IsVisible()
            || CopyLibrary.TypingInTextField()
            || !_key.IsDown())
        {
            return;
        }

        Piece piece = Hud.instance.m_hoveredPiece;
        if (piece == null || PieceCategories.IsSpecial(piece))
        {
            return;
        }

        bool hidden = MenuState.ToggleHidden(piece.gameObject.name);
        player.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize(
            hidden ? "$betterhammer_piece_hidden" : "$betterhammer_piece_unhidden", piece.m_name));

        // The game rebuilds the categories and pieces, which adds or drops the Hidden category.
        Hud.instance.m_hoveredPiece = null;
        ui.SelectPieceList(ui.m_currentPieceList, refreshOnly: true);
    }
}
