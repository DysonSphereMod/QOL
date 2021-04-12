# RailgunsRetargeting

With this mod installed your railguns will try to target an alternative orbit if the configured one is not reachable.

![RailgunsRetargeting](https://github.com/DysonSphereMod/QOL/blob/master/RailgunsRetargeting/screenshot.jpg?raw=true)

## Disclaimer

**This mod does not alter any entity or save anything to disk.**  
This mean the mod SHOULD be very safe to use and not break your savegame, not even after an update.  
If it stops working following a game update, you can just disable it and your game will just work, obviously without the extended capabilities provided by the mod.

## Configuration

This mod is configurable (you must start the game at least once for the configuration file to be created) using the following parameters:

### ignoreOrbit1

> Should the auto retargeting ignore the default, undeletable orbit 1 ?

-   `Default value : false (allow retargeting to orbit1)`

### forceRetargeting

> Should the auto retargeting be enabled also on non configured ejectors ?

-   `Default value : true (enable auto)`

## Changelog

### v1.3.1

-   Fix incorrect behaviour where non configured railguns where targeting orbit1 when `forceRetargeting` is set to `true` even if `ignoreOrbit1` is `true`

### v1.3.0

-   Add configuration option to toggle automatic autotargeting for unconfigured ejectors (enabled by default)

### v1.2.0

-   Add configuration option to disable retargeting to default, undeletable, `orbit 1`

### v1.1.0

-   Ensure the originally selected orbit (and not the retargeted one) is stored at save time
-   Improve visual style in railgun configuration panel

### v1.0.1

-   Initial Release

## Special thanks

-   thanks to NZ_Wanderer for beta testing this mod
-   <div>Icon made by <a href="https://www.freepik.com" title="Freepik">Freepik</a> from <a href="https://www.flaticon.com/" title="Flaticon">www.flaticon.com</a></div>
