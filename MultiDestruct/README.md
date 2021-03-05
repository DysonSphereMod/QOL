# MultiDestruct

This mods gives you the ability to build multiple copies of a building, with optional space between the copies.

This mod is **NOT COMPATIBLE** with AdvancedBuildDestruct.

![MultiDestruct](https://github.com/DysonSphereMod/QOL/blob/master/MultiDestruct/screenshot.jpg?raw=true)
> In this screenshot you can see [MultiDestruct](https://dsp.thunderstore.io/package/brokenmass/MultiDestruct/) , [CopyInserters](https://dsp.thunderstore.io/package/thisisbrad/CopyInserters/) and [BuildCounter](https://dsp.thunderstore.io/package/brokenmass/BuildCounter/);


## Usage

Select a building from the building bar or copy an exisiting building, then press `LEFT ALT` keyboard button to enter MultiDestruct mode.

`Left Mouse Click` once to start building and `Left Mouse Click` one more time when you are happy with the placement of the copies.

Press `+` and `-` on your keyboard to increase / decrease the spacing between buildings

Press `Z` to 'rotate' the building path (if you are not buildin on a straight line)


## FAQ

- **What are the limitations of this mod ?**  
  This mod doens't allow to place multiple miners, oil extractors or orbital collectors. 


- **What are the differences with AdvancedBuildDestruct ?**  
  This mod uses a 'native-like' collision detection ensuring that your building are always correctly placed. It also take control of the building loop and trigger complex recalculation only when needed thus increasing the performance in building mode (no more frame drops), expecially when used together with CopyInserters.


- **Why didn't you contribute to AdvancedBuildDestruct ?**   
  I've tried and opened a PR to the mod maintainer with an initial set of improvements. Unfortunately he/she doesn't seem to be too active and I decided to publish an alternative mod


## TODO

- Add ability to build a full 'loop' of a certain type of building. useful to build a lot of solar panels.
- Allow to build outside build range (acting as a sort of)
- Add 'blueprints' (allow to copy an area of the map and paste it somewhere else, while keeping the spacing between building consistent)

  
## Changelog

### v1.0.0
- Fixes an null pointer error in some cases when the spacing was set to a value higher than the amount of spaces between start and end
- Add ability to use keypad + and - to increase / decrease spacing
- Fix spacing of power poles

### v1.0.0
- Initial Release


## Special Thanks

Thanks to `iskabot` and `Ixosis` for the beta testing.