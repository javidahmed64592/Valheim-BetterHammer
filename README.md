[![CI](https://img.shields.io/github/actions/workflow/status/javidahmed64592/valheim-betterhammer/ci.yml?branch=main&style=flat-square&label=CI&logo=github)](https://github.com/javidahmed64592/valheim-betterhammer/actions/workflows/ci.yml)
[![Package](https://img.shields.io/github/actions/workflow/status/javidahmed64592/valheim-betterhammer/package.yml?branch=main&style=flat-square&label=Package&logo=github)](https://github.com/javidahmed64592/valheim-betterhammer/actions/workflows/package.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

# BetterHammer

This mod adds several improvements to the vanilla base hammer:

- **No hammer required**: Open the build menu without a hammer in your inventory by pressing `B`.
- **Use nearby materials**: Use materials from the ground and nearby chests when your inventory runs short.
- **Build grid**: Toggle the build grid by pressing `Ctrl+Right-Click`. Adjust the grid distance by scrolling while holding `Ctrl`.
- **Copying structures**: Select built pieces to copy by looking at them and pressing `Alt+Middle-Click`, and deselect them with `Alt+Right-Click`. Once you have selected the pieces you want to copy, save them as a copy in the Copies tab with `Alt+Left-Click`.
- **Move pieces**: Move individual built pieces or entire selections with `Ctrl+Middle-Click`. Then left-click to put them down at a new location.
- **Repair mode**: Toggle repair mode by pressing `Shift+Right-Click`.
- **Zoom camera**: Zoom the camera in and out without needing to unequip the hammer by scrolling while holding `Alt`.

## Build Grid

While placing a piece, a grid is drawn on the surface you are aiming at and pieces snap to its vertices.

- The grid aligns with the terrain and surfaces except for diagonal surfaces.
- The chunk borders are drawn as red lines, and the centrelines of the chunk are green.

![Chunk Lines](https://github.com/javidahmed64592/Valheim-BetterHammer/raw/main/images/game_screenshot_1.png)
![Chunk Edges](https://github.com/javidahmed64592/Valheim-BetterHammer/raw/main/images/game_screenshot_2.png)

## Copying and Moving

Look at built pieces and press `Alt+Middle-Click` to select them. Then:

| Key                                     | Action                                                       |
| --------------------------------------- | ------------------------------------------------------------ |
| `Alt+Left-Click`                        | Save the selection as a copy in the hammer's **Copies** tab. |
| `Ctrl+Middle-Click` on a selected piece | Move the whole selection.                                    |
| `Alt+Right-Click`                       | Deselect the piece you are looking at.                       |
| `Backspace`                             | Clear the selection.                                         |
| `Delete`                                | Destroy every selected piece, dropping their materials.      |

- Build a saved copy like any other piece. Hold `Alt` to see the selection's total materials.
- `Ctrl+Middle-Click` on a piece that isn't selected moves just that piece. Left-click to put it down, right-click to cancel.
- Moving is free and keeps everything inside, so chests don't need emptying. Ward and workbench rules apply, and ships and carts can't be moved.
- While placing a copy or a moved selection, `Q`/`E` cycles snap points and `Shift+Q`/`Shift+E` switches which piece you snap by.
- Copies are saved in `BepInEx/config/BetterHammer/copies.json` for all your characters and worlds. To delete one, hover it in the build menu and press `Delete`.

![Multi Piece Copy](https://github.com/javidahmed64592/Valheim-BetterHammer/raw/main/images/game_screenshot_3.png)

## Configuration

Settings are in the BepInEx config file `javidahmed64592.betterhammer.cfg`.
Settings marked "(Synced with server)" use the server's values when the server has the mod installed.

## Compatibility

- Mods that also build from nearby chests or ground items (such as AzuCraftyBoxes, or the craft from chests option in Valheim Plus) overlap with this mod's materials feature. Turn one of them off: here, set `Use Nearby Chests` to false and the ground items `Radius` to 0.
- Mods that change how pieces snap or add their own build key hints may clash with the build grid or its hints.
- This mod has not been tested with a gamepad, only with keyboard and mouse.

## Uninstalling

Nothing is saved to your world or character, so the mod can be removed at any time.

## License

This project is licensed under the MIT License - see the [LICENSE](https://github.com/javidahmed64592/Valheim-BetterHammer/blob/main/LICENSE) file for details.
