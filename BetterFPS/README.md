# BetterFPS

This mod should improve megafactories FPS. By default all optimisations are disabled, so ensure to configure it correctly (after an initial game start for the config file creation)

## Configuration

This mod is fully configurable (you must start the game at least once for the configuration file to be created) using the following parameters:

### hideDysonSphereMesh

> If this option is set to true, the mesh of dyson spheres will not be rendered as this rendering part has not yet been optimised by the devs

-   `Type: boolean [true/false]`
-   `Default value : false`

### parallelFactories

> If this option is set to true, the factories production calculation will be executed in parallel. This option gives the best fps benefit but , as this mod is still quite young, might randomly crash your game (it never crashed mine but prefer to be cautious here).

-   `Type: boolean [true/false]`
-   `Default value : false`

### disableShadows

> If this option is set to true, the game will not render object shadows.

-   `Type: boolean [true/false]`
-   `Default value : false`

## Changelog

### v1.0.2

-   FIX: should definitely fix crash when completing researches and another dyson spheres edge condition
-   FIX: should fix random crash when adding/removing stuff from containers

### v1.0.1

-   FIX: Should fix crash when completing researches and another dyson spheres edge condition

### v1.0.0

-   Initial Release

<div>Icons made by <a href="https://www.flaticon.com/authors/vectors-market" title="Vectors Market">Vectors Market</a> from <a href="https://www.flaticon.com/" title="Flaticon">www.flaticon.com</a></div>
