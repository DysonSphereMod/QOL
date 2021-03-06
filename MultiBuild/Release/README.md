# MultiBuild

This mods gives you the ability to build multiple copies of a building, with optional space between the copies.
It also allow you to create / restore/ export/ import blueprints, allowing you to copy and paste whole parts of your base
Please carefully read the FAQ section.

Visit the amazing website created by `Diacred` [https://www.dysonsphereblueprints.com](https://www.dysonsphereblueprints.com) to view and share blueprints!

This mod is **NOT COMPATIBLE** with `AdvancedBuildDestruct` and `Copyinserters`, but that shouldn't be a problem as this mod provides the same functionalities (multibuild, copy of inserters, etc).

The mod will not initialise if any of the incompatible mods are loaded. please remove them from your modman profile or from your plugins (if you are installing mods manually)

![MultiBuild](https://github.com/DysonSphereMod/QOL/blob/master/MultiBuild/screenshot.jpg?raw=true)

> In this screenshot you can see [MultiBuild](https://dsp.thunderstore.io/package/brokenmass/MultiBuild/) in multibuild mode and [BuildCounter](https://dsp.thunderstore.io/package/brokenmass/BuildCounter/);

## Release channels

This mod is released on 2 separate "channels":

-   [MultiBuild](https://dsp.thunderstore.io/package/brokenmass/MultiBuild/): The stable version of this mod. Only when functionalities have reached a target level of stability they are promoted to this channel. This mean that this version is updated less often than the other. Consider this the `SLOW CHANNEL` and choose this version if you prefer stability over functionalities.

-   [MultiBuildBeta](https://dsp.thunderstore.io/package/brokenmass/MultiBuildBeta/): The 'bleeding' edge version of the mod, aligned to the most recent version of the code. While still being highly reliable, when using this versions you might experience some 'weirdness', graphic glitches and, very rarely, an error message. Consider this the `FAST CHANNEL` and and choose this version if you want the latest functionalities and/or if you want to contribute to the development of this mod.

The two channels are not compatible with each other and none of the 2 will load if they are both loaded at the same time.

## Disclaimer

**This mod does not alter any entity or save anything to disk.**  
This mean the mod SHOULD be very safe to use and not break your savegame, not even after an update.  
If it stops working following a game update, you can just disable it and your game will just work, obviously without the extended capabilities provided by the mod.

## Usage

### MultiBuild Mode

Select a building from the building bar or copy an existing building, then press `LEFT ALT` keyboard button to enter multibuild mode.

This mode is disabled while holding a blueprint (for now).

-   `Left Mouse Click` once to start building and `Left Mouse Click` one more time when you are happy with the placement of the copies.
-   Press `TAB` to toggle the pasting of the copied inserters
-   Press `+` and `-` on your keyboard to increase / decrease the spacing between buildings
-   Press `CTRL +` and `CTRL -` on your keyboard to increase / decrease the spacing period (number of copied before adding a space)
-   Press `0` to reset spacing to 0 and spacing period to 1
-   Press `Z` to 'rotate' the building path (if you are not building on a straight line)

### Blueprint Mode

Click the blueprint button in the build dock

-   The first button initiate the creation of a new blueprint
    -   `left click` (and hold if you want) to add entities to the blueprint
    -   hold `CTRL` and `left click` (and hold if you want) to remove entities from the blueprint
    -   hold `ALT` and `left click` to define the blueprint reference building. This is the "center" of your blueprint and where the cursor will be positioned when pasting.
    -   `right click` to exit blueprint creation mode
-   The second button restore the last used blueprint
-   The 3rd button exports the current blueprint to your clipboard
-   The 4th button import the data from your clipboard into your blueprint

### Inserter copy functionality

When `shift+click` an existing building it will be copied with all the connected inserters (creating a minimal blueprint , that you can store and reload);

## Configuration

### itemSpecificSpacing

> If this option is set to true, the mod will remember the last spacing used for a specific building. Otherwise the spacing will be the same for all entities.

-   `Type: boolean [true/false]`
-   `Default value : true`

## FAQ

-   **What are the limitations of this mod (MultiBuild mode)?**  
    This mod doesn't allow to place multiple miners, oil extractors or orbital collectors.

-   **What are the limitations of this mod (Blueprint mode)?**  
    This mod is still in development and so has one or more know issue:
    -   in some conditions, when pasting a blueprint, it's possible to place building nearer than what is possible in vanilla

## TODO

-   Add ability to build a full 'loop' of a certain type of building. useful to build a lot of solar panels.

## Changelog

### v2.3.8

-   FIX: Fix incorrect placement of entities when `shift + clicking` and multibuilding with non 0 spacing
-   FIX: Fix edge case ArgumentException error

### v2.3.7-Beta

-   FEAT: Allow to blueprint and paste stacked buildings
-   FEAT: Copy storage size setting (only when blueprinting and not when `shift + clicking` to avoid mistakes)

### v2.3.6-Beta

-   FIX: Fix issue where moving huge blueprints around could crash the game :O

### v2.3.5-Beta

-   FIX: Restore the ability to build lines of daisy chained buildings in multibuild mode

### v2.3.4-Beta

-   FEAT: Allows to define the blueprint reference building by holding `ALT` and clicking a building. The reference will be the 'center' of your blueprint
-   FIX: Fix issue where the mod was not correctly checking if you had enough items to paste the blueprint (especially with inserters)
-   FIX: Silence error message related to buffer limits when pasting huge blueprints (>8k belts)

### v2.3.3-Beta

-   FIX: Fix error occurring when `shift + clicking` stacked buildings

### v2.3.2-Beta

-   FIX: Fix regression issue with limited build area when pasting a blueprint

### v2.3.1-Beta

-   FIX: Restore missing visual indicator for blueprint/ multibuild mode & spacing

### v2.3.0-Beta

-   FEAT: Correctly copy Splitter settings (FINALLY) (only when blueprinting and not when `shift + clicking` to avoid mistakes)
-   FEAT: Correctly copy Logistic station settings (only when blueprinting and not when `shift + clicking` to avoid mistakes)
-   FEAT: Rewritten multi-threading logic to further improve fps with big blueprints
-   FIX: Improve compatibility with [Touhma GalacticScale](https://dsp.thunderstore.io/package/Touhma/Touhma_GalacticScale/) (thanks innominata)

### v2.2.1

-   FIX: Fix compatibility with game version 0.6.17.6112

### v2.2.0

-   FEAT: MultiBuild is now MultiThreaded. You should notice an improvements in performance when pasting large blueprints / long lines of buildings
-   FIX: fix 2 bugs (`NPE` and `array index outOfBounds`) happening when blueprinting unbuilt buildings or buildings with unbuilt inserters

### Previous versions

For older changelog entries visit https://github.com/DysonSphereMod/QOL/blob/beta/MultiBuild/OLD_CHANGELOG.md

## Special Thanks

Thanks to `Kremnev8` for the amazing work on the UI !
Thanks to `Nordblum`, `Fury_Fairy`, `iskabot` , `Ixosis`, `NZ_Wanderer`, `Kodu`, `Wingless` for the closed beta testing.
