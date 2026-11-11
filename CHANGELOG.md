# Changelog

## Unreleased

- Added selecting connected pieces: `Alt+Shift+Middle-Click` on a piece selects it and every piece connected to it, directly or through other pieces, and `Alt+Shift+Right-Click` deselects them. Pieces count as connected when they touch, as the game sees it for support. The ground doesn't connect them. Pieces this hammer can't build are left out but still link the pieces around them. A search stops at 1000 pieces, set by the new `Max Connected Pieces` setting.
- `Shift+Right-Click` and `Ctrl+Right-Click` no longer toggle repair mode or the grid while `Alt` is held.
- `Ctrl+Right-Click` toggles the build grid while moving pieces, instead of aborting the move.

## 1.0.1

- Building a saved copy now keeps the reference piece and snap point you cycled to with `Q`/`E`, instead of going back to automatic snapping after every placement, so several copies can be placed in a row from the same snap point. Like a single piece, the copy also keeps them when the hammer is put away and taken out again.

## 1.0.0

- Renamed the mod from NoHammerBuildMenu to BetterHammer, to reflect everything it does now.
- If you have NoHammerBuildMenu installed, uninstall it before installing BetterHammer. Running both at once would apply every feature twice.
- The plugin GUID changed to `javidahmed64592.betterhammer`, so BetterHammer starts with a fresh config file (`javidahmed64592.betterhammer.cfg`). Settings from `javidahmed64592.nohammerbuildmenu.cfg` aren't carried over.
- A custom key for "Open Build Menu" set in your mod configuration resets to `B` once.
- `Ctrl+Mouse3` (middle-click) on a piece selected for copying now moves the whole selection. It works like moving a single piece: everything stored in the pieces comes with them, nothing is dropped, the pieces glow gold, and right-click cancels. The ghost behaves like a saved copy's, so `Q`/`E` and `Shift+Q`/`Shift+E` cycle snap points and pieces. The moved pieces stay selected.
- Saving a selection with `Alt+Mouse1` (left-click) now asks for a name. Leave it as it is, or blank, for the default "Copy N". Cancelling saves nothing.
- Saved copies can be renamed: hover one in the build menu and press `Insert` (configurable as `Rename Copy`).
- Updated the mod description.
- Moved the source files into folders by feature (`Core`, `Grid`, `Materials`, `Structures`, `UI`, `Patches`), leaving only `Plugin.cs` at the root (no behaviour change).

## 0.15.0

- Added copying several pieces at once. Look at a piece and press `Alt+Mouse3` (middle-click) to select it, or `Alt+Mouse2` (right-click) to deselect it. Then press `Alt+Mouse1` (left-click) to save the selection as a new piece in the hammer's **Copies** tab ("Copy 1", "Copy 2", ...), with a picture of the whole copy as its icon.
- Building a saved copy shows a placement ghost of every piece, keeping their places relative to each other, and builds them all with one click. Like any piece, it stays selected when the hammer is put away and taken out again.
- Saved copies are kept in `BepInEx/config/NoHammerBuildMenu/copies.json`, shared by all characters and worlds. Hover one in the build menu and press `Delete` to delete it, after a confirmation.
- With pieces selected, `Backspace` clears the selection and `Delete` removes every selected piece after a confirmation, dropping their materials as the hammer would. Both keys are configurable (`Clear Selection` and `Delete`).
- Moving a selected piece keeps it selected, so a copy saved afterwards uses its new spot.
- Selected pieces glow teal. The selection survives putting the hammer away, and pieces that are removed or destroyed drop out of it.
- Holding `Alt` shows the selection in the build panel with the combined materials and whether you have enough (nearby chests and ground items included). Saved copies are shown the same way. Long material lists page through a few at a time.
- While placing a copy, `Q`/`E` steps through every piece's snap points in turn, and `Shift+Q`/`Shift+E` jumps from piece to piece.
- Added key hints for selecting, deselecting, saving, clearing and removing the selection, and snapping to another piece.

## 0.14.0

- Added moving built pieces: look at a piece and press `Ctrl+Mouse3` (middle-click) to pick it up, then left-click to put it down or right-click to cancel. Chest contents, damage, sign text and the rest of the piece's state move with it, and nothing is dropped.
- The piece being moved glows gold, pulsing, so it's easy to keep track of.
- A piece can be put down overlapping its old spot, so it can be nudged a little without moving it away and back.
- A message says when a piece has been moved, and when a move is aborted and why (right-click, the piece was removed or destroyed, the hammer was put away, or a different piece was selected).
- The piece being moved can't be removed with middle-click while moving it, so a misclick can't drop a chest's contents. Other pieces can still be removed.
- Added a key hint for moving pieces, shown in repair mode too. While moving a piece, the right-click hint says "Abort" instead of "Build Menu".

## 0.13.0

- Building no longer takes materials from a chest another player has open. Previously the chest's contents could get out of step between the two players, which could duplicate or lose items.
- Chests now follow the same privacy rules as opening them yourself, so chests shared with your group are used too.
- Items on the ground are only used once your game has been handed them, the same way picking an item up works, so another player can't pick up an item that was also used to build. This normally takes a moment.
- `Use Nearby Chests`, the chest `Radius` and the ground items `Radius` are now synced from the server when it has the mod.
- Declared the mod as client side to Jotunn, so servers and other players are never required to have it installed.
- Messages and key hints can now be translated (`Translations/English.json`).
- Added a CI check that the version matches in every file.
- Moved `package.sh` into `.github/scripts` and made it the same for every mod: the name, version and DLL now come from `manifest.json` and the `.csproj`.

## 0.12.0

- The requirement list in the build panel now shows the total available before the amount needed (e.g. `Wood 64/2`). The total includes your inventory, nearby chests and ground items within range.

## 0.11.1

- Updated the BepInExPack_Valheim dependency to 5.4.2360.

## 0.11.0

- Added an `.editorconfig`, `Directory.Build.props` and a CI workflow that runs `dotnet format`, a Release build and a NuGet vulnerability check.
- Reformatted the code to match the new code style rules (no behaviour change).
- Moved the Harmony patches into the `NoHammerBuildMenu.Patches` namespace.
- Tidied up packaging scripts and workflow files.
- Added a `.gitattributes` to keep line endings consistent.
- Moved the local Valheim install paths into `Directory.Build.props`; set `VALHEIM_DIR` and `BEPINEX_PROFILE_DIR` once to build every mod without editing the `.csproj`.
- Updated README and icon.

## 0.10.1

- Increased the default radius for nearby ground items from 10m to 30m.

## 0.10.0

- The hammer now uses items on the ground within a configurable range.
- This can be adjusted by changing the `Radius` setting under the `Ground Items` section in the config file.
- The build grid lines at 2m intervals now use the configured `Line Width`, and the lines in between are thinner (`Minor Line Width Scale`, default 0.5, set to 1 for uniform lines), making 2m distances (the size of many pieces) easy to read at any grid size.

## 0.9.0

- The build grid now draws a distinct line through the centre of each world chunk, making it easy to find a chunk's centre. Its width and colour can be changed with the new `Chunk Centre Line Width` and `Chunk Centre Line Color` options in the `Grid Appearance` config section.
- Moved the Harmony patches out of `Plugin.cs` into dedicated files (no behaviour change).
- Updated README.

## 0.8.1

- Increased the default radius for nearby chests from 20m to 30m.
- Updated README.

## 0.8.0

- Building now takes materials from nearby chests as well as your inventory. Your own stock is used first and only the shortfall comes from chests (nearest first), and a piece can only be built if your inventory and nearby chests combined have enough. The build panel shows the combined amount.
- Only player-built chests you could normally open are used (not other players' private chests, chests in use, or ones in a ward you can't access).
- Added a new `Chests` config section with `Use Nearby Chests` and `Radius` (default 20m).

## 0.7.0

- The mod's controls (repair toggle, build grid toggle, grid distance and camera zoom) now show in the build key hints at the bottom of the screen, like the vanilla ones. They only show while a hammer patched by the mod is equipped, and not when using a gamepad.

## 0.6.0

- The build grid now snaps and draws on whichever face of a piece you are looking at: the top (as before), the underside (e.g. ceilings), and vertical walls, so pieces can be aligned along all axes.
- Snapping only moves the aim point within the targeted surface, so pieces never snap to empty air. Diagonal walls only snap in height and show no grid.
- Terrain and non-piece objects keep snapping horizontally as before.

## 0.5.0

- Decreased the default brightness of the build grid lines.
- Made the build grid appearance configurable in the new `Grid Appearance` config section: radius, fade start, rebuild distance, lift, segment length, line widths, and line colours.

## 0.4.0

- Highlight world chunk borders in a different colour.
- Make the build grid thicker and more visible.
- Shift+right-click (repair toggle) and Ctrl+right-click (grid toggle) no longer also open the build menu when using the vanilla hammer or other hammers, not just the phantom hammer.

## 0.3.0

- Hold Alt and scroll to zoom the camera in and out while a hammer is equipped and placing a piece, so you no longer need to unequip the hammer to get a closer look. Alt+scroll does not rotate the piece.

## 0.2.0

- Added a world build grid: a visible grid overlay near your aim point (following the terrain, or flat on top of player-built floors) with snap points at every vertex. Pieces snap to grid vertices like they do to other pieces' snap points, and snapping to other pieces still takes priority.
- Hold Ctrl and scroll to double or halve the grid distance (0.25m, 0.5m, 1m, 2m, 4m; 0.5m default).
- The grid only applies while a hammer is equipped (the phantom hammer, the vanilla hammer, or modded hammers with "hammer" in their name), not the hoe or cultivator.
- Whatever the game uses as the piece's anchor at the aim point (the selected snap point, or in `Auto` the part of the piece closest to the surface you are aiming at) is the part placed on the grid vertex.
- Press Ctrl + Right Mouse Button while building to toggle the grid. Enabled state and distance are in the `Grid` config section.

## 0.1.5

- Added repair mode toggle keybind (Shift + Right Mouse Button) to switch between repair mode and the last selected build piece.

## 0.1.4

- Ensure hammer recipes are known without needing to hold a physical hammer

## 0.1.3

- Fix website link in manifest file
- Include CHANGELOG.md in the packaged mod files

## 0.1.2

- Made hammer invincible so it does not break
- The hammer is no longer invisible when equipped in the player's hand

## 0.1.1

- Fixed README with example screenshot

## 0.1.0

- Initial release
- Configurable keybind (default `B`) opens the build menu without a hammer in your inventory, and shows the hammer in-hand
- Keybind is rebindable from your mod manager
