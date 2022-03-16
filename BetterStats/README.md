# BetterStats

This mod improves the production stats panel to add the count of producer/consumer and the maximum theoretical production/consumption (i.e.: the maximum you can produce if you have no power issue or input/output bottleneck), taking into account mining productivity multipliers.

![BetterStats-1](https://github.com/DysonSphereMod/QOL/blob/master/BetterStats/screenshot.jpg?raw=true)

# Configuration

After a first launch you will find the configuration file `BepInEx\config\com.brokenmass.plugin.DSP.BetterStats.cfg`.

You can customise the production ratios that govern text highlighting (red for lack of production ; yellow when max consumption is way above max production)

## Changelog

### v1.3.3

-   FIX: Updated counter to record the theoretical max consumption rate for Universe Matrix. (thanks mattsemar)
-   FIX: Updated the display/s checkbox to be persisted as a config property. (thanks mattsemar)

### v1.3.2

-   FIX: Adjust all time stat view to differentiate between amounts and rates. (thanks mattsemar)
-   FIX: Add consumption for antimatter fuel rods. (thanks mattsemar)

### v1.3.1

-   FIX: fix compatibility with latest game version (thanks mattsemar)

### v1.3.0

-   FEAT: Highlights in red items where production is insufficient (Thanks ThomasBlt)
-   FIX: fix error if the game 'language' uses . as decimal separator instead of , (thanks ThomasBlt)

### v1.2.0

-   FEAT: allow to filter stats by item name
-   FEAT: Count critical photon production (thanks zhangxp1998)

### v1.1.2

-   FIX: Fix fractionators incorrectly reporting production as consumption
-   FIX: Limit miners production to max output possible (30item/s max belt speed) (thanks zhangxp1998)

### v1.1.1

-   FEAT: Include production of Orbital Collector stations

### v1.1.0

-   FEAT: Improved UI
-   FEAT: Allows to display stats per seconds instead of per minute (thanks wingless)
-   FEAT: Shows number of producer and consumers

### v1.0.0

-   Initial Release

## To build this mod

Inspired by https://docs.bepinex.dev/master/articles/dev_guide/plugin_tutorial/1_setup.html

### Setup visual studio

-   Install Visual Studio (not visual studio code)
-   In Visual Studio, create a new blank project and there choose "add more tools" and add .Net development support
-   Install .Net Framework 3.5 (be careful to choose "Framework")

### project setup

-   In Visual Studio, choose import and then select "BetterStats.csproj"
-   Game libraries should be loaded from NuGet 

### Create a release
-   From a PowerShell prompt run `.\package -vertype ` with either major, minor, patch or 'none'. 'none' will not increment the version number 
-   Uploadable zip file will be placed in .\tmp_release   
<div>Icon made by <a href="https://www.freepik.com" title="Freepik">Freepik</a> from <a href="https://www.flaticon.com/" title="Flaticon">www.flaticon.com</a></div>
