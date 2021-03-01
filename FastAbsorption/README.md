# FastAbsorption

![FastAbsorption Demo](https://github.com/DysonSphereMod/FastAbsorption/blob/master/FastAbsorption.gif?raw=true)

This mod increase Dyson sphere absorption frequency of solar sails (defaults to one sail every 2 second).

No more waiting for those puny sails to find their way to a node !  

The only limits are your production and shooting speed!  


## Configuration

The speed up is configurable (you must start the game at least once for the configuration file to be created) using the following parameters:

### frequencyMultiplier
> How much more frequently should sail be requested by every DysonSphere node.
- `Min Value     : 1 (one sail every 2 second)`
- `Max value     : 120 (one sail every frame)`
- `Default value : 10`

### travelSpeedMultiplier
> How much faster do sails take to travel to the requesting node.  
> Values greater than 1 make the sail teleport to the proximity of the target node.
- `Min Value     : 1 (4 minutes travel time)`
- `Max value     : 120 (2 seconds travel time)`
- `Default value : 1`
  
  
_Please  restart your game to apply any configuration changes._
  
  
## Changelog

### v1.1.0
- Add frequency and travel time multiplier configuration

### v1.0.0
- Initial Release



-----

Special thanks to Nordblum for the mod icon.