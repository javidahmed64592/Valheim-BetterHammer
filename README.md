[![CI](https://img.shields.io/github/actions/workflow/status/javidahmed64592/valheim-nohammerbuildmenu/ci.yml?branch=main&style=flat-square&label=CI&logo=github)](https://github.com/javidahmed64592/valheim-nohammerbuildmenu/actions/workflows/ci.yml)
[![Package](https://img.shields.io/github/actions/workflow/status/javidahmed64592/valheim-nohammerbuildmenu/package.yml?branch=main&style=flat-square&label=Package&logo=github)](https://github.com/javidahmed64592/valheim-nohammerbuildmenu/actions/workflows/package.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

# NoHammerBuildMenu

This mod adds several improvements to the vanilla base hammer:

| Key                          | Action                                                            |
| ---------------------------- | ----------------------------------------------------------------- |
| `B`                          | Open the build menu without a hammer in your inventory.           |
| `Ctrl+Mouse2` (right-click)  | Toggle the build grid.                                            |
| `Ctrl+Scroll`                | Adjust the grid distance.                                         |
| `Shift+Mouse2` (right-click) | Toggle repair mode.                                               |
| `Alt+Scroll`                 | Zoom the camera in and out without needing to unequip the hammer. |
| `Ctrl+Mouse3` (middle-click) | Move the piece you are looking at.                                |
| `Alt+Mouse3` (middle-click)  | Select the piece you are looking at for copying.                  |
| `Alt+Mouse2` (right-click)   | Deselect the piece you are looking at.                            |
| `Alt+Mouse1` (left-click)    | Copy all the selected pieces.                                     |

Additionally, the hammer also uses materials from the ground and nearby chests when your inventory runs short.
The total quantity of each required material is displayed, including items in your inventory, nearby chests, and on the ground within range.

## Build Grid

While placing a piece, a grid is drawn on the surface you are aiming at and pieces snap to its vertices, in the same way they snap to the snap points of other pieces.

- On terrain, the grid follows the ground and pieces snap horizontally, with height coming from the ground.
- On horizontal and vertical surfaces, the grid aligns with the surface and pieces snap accordingly.
- The grid does not appear on diagonal surfaces and pieces only snap within the surface you are aiming at.
- The chunk borders are drawn as red lines, and the centrelines of the chunk are green.

![Chunk Lines](https://github.com/javidahmed64592/Valheim-NoHammerBuildMenu/raw/main/images/game_screenshot_1.png)
![Chunk Edges](https://github.com/javidahmed64592/Valheim-NoHammerBuildMenu/raw/main/images/game_screenshot_2.png)

## Moving Pieces

Look at a built piece and press `Ctrl+Mouse3` (middle-click) to pick it up.
Left-click to put it down there, or right-click to cancel.

- Moving is free, and everything stored in the piece comes with it, so chests and machines don't need emptying first.
- You can move a piece if you could remove it with the hammer: ward and workbench rules apply. Chests also need you to be allowed to open them, and can't be moved while someone has them open.
- Middle-click doesn't remove the piece you're moving, so a misclick can't spill a chest. Other pieces can still be removed while moving.
- Ships, carts and items placed as decorations can't be moved.

## Copying Several Pieces

Look at a built piece and press `Alt+Mouse3` (middle-click) to select it for copying, and repeat for as many pieces as you like.
Selected pieces glow teal, and `Alt+Mouse2` (right-click) deselects the piece you are looking at.
When you're ready, press `Alt+Mouse1` (left-click) to pick up a placement ghost of all of them, then left-click to build the copy, or right-click to put it away.

- Hold `Alt` to see the selection in the build panel, with the materials needed to build all of it.
- `Q`/`E` steps through the snap points of each piece in turn, and `Shift+Q`/`Shift+E` jumps from piece to piece.
- The copy stays in hand after building it, so you can build it again. The selection is cleared by the first copy you build.
- You can keep building and selecting as normal until you copy the selection, and putting the hammer away doesn't clear it. A selected piece that is removed or destroyed is dropped from the selection.

## Configuration

Settings are in the BepInEx config file `javidahmed64592.nohammerbuildmenu.cfg`.
Settings marked "(Synced with server)" use the server's values when the server has the mod installed.

## Compatibility

- Mods that also build from nearby chests or ground items (such as AzuCraftyBoxes, or the craft from chests option in Valheim Plus) overlap with this mod's materials feature. Turn one of them off: here, set `Use Nearby Chests` to false and the ground items `Radius` to 0.
- Mods that change how pieces snap or add their own build key hints may clash with the build grid or its hints.
- This mod has not been tested with a gamepad, only with keyboard and mouse.

## Uninstalling

Nothing is saved to your world or character, so the mod can be removed at any time.

## License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/javidahmed64592/Valheim-NoHammerBuildMenu/blob/main/LICENSE) file for details.
