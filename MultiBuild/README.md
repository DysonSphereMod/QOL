# MultiBuild

This mods gives you the ability to build multiple copies of a building, with optional space between the copies.
It also allow you to create / restore/ export/ import blueprints, allowing you to copy and paste whole parts of your base

This mod is **NOT COMPATIBLE** with AdvancedBuildDestruct and Copyinserters, but that shouldn't be a problem as this mod provides the same functionalities (multibuild, copy of inserters, etc).

The mod will not initialise if any of the incompatible mods are loaded. please remove them from your modman profile or from your plugins (if you are installing mods manually)

![MultiBuild](https://github.com/DysonSphereMod/QOL/blob/master/MultiBuild/screenshot.jpg?raw=true)
> In this screenshot you can see [MultiBuild](https://dsp.thunderstore.io/package/brokenmass/MultiBuild/) in multibuild mode and [BuildCounter](https://dsp.thunderstore.io/package/brokenmass/BuildCounter/);


## Beta Notice

This version is under development and cannot , yet, be considered fully stable. It should not affect in any destructive manner your savegame, but you might experience some 'weirdness' and graphic glitches when using this mod. 

Please carefully read the limitation of the current beta version of this mod.


## Disclaimer

**This mod does not alter any entity or save anything to disk.**  
This mean the mod SHOULD be very safe to use and not break your savegame, not even after an update.  
If it stops working following a game update, you can just disable it and your game will just work, obviously without the extended capabilities provided by the mod.


## Usage




### MultiBuild Mode
Select a building from the building bar or copy an exisiting building, then press `LEFT ALT` keyboard button to enter multibuild mode.

This mode is disabled while holding a blueprint (for now).

`Left Mouse Click` once to start building and `Left Mouse Click` one more time when you are happy with the placement of the copies.

Press `+` and `-` on your keyboard to increase / decrease the spacing between buildings

Press `0` to reset spacing to 0

Press `Z` to 'rotate' the building path (if you are not buildin on a straight line)


### Blueprint Mode

Click the blueprint button in the build dock

- The first button initiate the creation of a new blueprint
  - `left click (and hold)` to add entities to the blueprint
  - `hold control` and `left click (and hold)` to remove entities from the blueprint
  - `right click` to exit blueprint creation mode

- The second button restore the last used blueprint 
- The 3rd button exports the current blueprint to your clipboard 
- The 4th button import the data from your clipboard into your blueprint

### Inserter copy functionality
When `shift+click` an exisisting building it will be copied with all the connected insters (creating a minimal blueprint , that you can store and reload);

## Configuration

### itemSpecificSpacing
> If this option is set to true, the mod will remember the last spacing used for a specific building. Otherwise the spacing will be the same for all entities.
- `Type: boolean [true/false]`
- `Default value : true`

## FAQ

- **What are the limitations of this mod (MultiBuild mode)?**  
  This mod doens't allow to place multiple miners, oil extractors or orbital collectors. 


- **What are the limitations of this mod (Blueprint mode)?**  
  This mod is still in development and so has one or more know issue:
  - positionin of entities around the poles is a bit finicky
  - in some conditions, when pasting a blueprint, it's possible to place building nearer than what is possible in vanilla
  - splitter / logistic stations / boxes settings are not copied



## TODO

- Add ability to build a full 'loop' of a certain type of building. useful to build a lot of solar panels.
- Allow to build outside build range (acting as a sort of)

  
## Changelog

### v2.0.0-Beta
- Blueprints !!!

### v1.1.2
- Fix incompatibility issue with CopyInserters where inserters were not being copied when in multibuild mode

### v1.1.1
- Fix a bug where the build preview disappears and the game reports "building out of range", if, when in multibuild mode, only a single copy of the building is placed.

### v1.1.0
- Store spacing indipendently per item so that you can place your power poles with a different spacing than your assembler without having to constantly tap `+` and `-`. You can opt out of this functionality and revert to the hold behavious by setting the `itemSpecificSpacing` config to `false`.
- Add ability to use key `0` to reset spacing

### v1.0.2
- Fixes incorrect (slightly large) spacing of accumulators

### v1.0.1
- Fixes an null pointer error in some cases when the spacing was set to a value higher than the amount of spaces between start and end
- Add ability to use keypad + and - to increase / decrease spacing
- Fix spacing of power poles

### v1.0.0
- Initial Release


## Special Thanks

Thanks to `Kremnev8` for the amazing work on the UI !
Thanks to `Nordblum`, `Fury_Fairy`, `iskabot` , `Ixosis`, `NZ_Wanderer`, `Kodu`, `Wingless` for the closed beta testing.