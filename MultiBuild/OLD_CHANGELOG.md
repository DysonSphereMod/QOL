## Old Changelog

### v2.1.4-Beta

-   Fix error when hovering a multilevel tank while holding a belt in hand

### v2.1.3-Beta

-   Fix issue with colliders in multibuild mode where shift+clicking a rotated entity and using multibuild mode was leading to either too spaced or not spaced enough copies (the collider of the copies was not correctly rotated)
-   Fix issue that allowed to connect inserter to building without inserter slots (like tesla towers for example), when copying building with attached inserters

### v2.1.2-Beta

-   Allow to import blueprintV1 ! (thanks `Kremnev8`)
-   fix edge case bug that could led to belt not correctly connecting to tanks / splitter / logistic towers
-   Add checks for incompatible items (modded items) when loading a blueprint

### v2.1.1-Beta

-   Allow to copy / paste elevated belts (multilevel buildings are still ignored even if they appear as selected)
-   Allow to connect blueprinted belts with existing ones (only at belt extremities and direction must, obviously, match)
-   Allow belt to belt inserter in blueprint as long as both start and end belt are selected

### v2.1.0-Beta

-   BREAKING CHANGE: Blueprint data format v2: 80% blueprint size reduction.
-   new Spherical coordinates positioning system. AMAZING work by `Kremnev8`
-   General code cleanup and big performance improvements across the board
-   fix bug where building of a blueprints could be placed on water if positioned outside the mecha building range
-   fix blueprint menu not closing in sail mode

### v2.0.6-Beta

-   Fix some buildings (power exchanger / ejectors / lab / rayreceivers) 'recipe' not being correctly copied in blueprint mode
-   Add ability to configure spacing period in multibuild using `CTRL +` and `CTRL -`
-   Add ability to toggle pasting of inserters in single and multibuild mode (not in blueprint mode) using `TAB`

### v2.0.5-Beta

-   Fix unresponsive blueprint UI

### v2.0.4-Beta

-   Fix an issue in blueprint mode where some buildings (tanks / fractionators / powerExchangers) were not correctly connecting to belts if the connected belt was built before the building itself
-   Blueprint panel shows correct info about last stored blueprint
-   Add ability to prepend blueprint strings with custom text to allow to quick identification of a stored blueprint. A `:` must terminate the custom string Example `Blueprint name:<actual blueprint string>`. The custom text, if present, will be shown in the panel info if an imported blueprint is stored.
-   Change visual notification when importing/exporting from blocking popup to less intrusive realtime popup notification

### v2.0.3-Beta

-   Fix an edge case issue where buildings where not copied with the correct yaw (happen if you copy an area containing belts that was created by a rotated blueprint, and the first element you select is a belt :S)
-   Fix blueprint data not correctly resetting when leaviing build mode

### v2.0.2-Beta

-   Logistic station settings and slot filters are now correctly copied and pasted (not when `shift + clicking` to avoid mistakes)
-   Fix an issue where some buildings 'recipe' was not being copied (ejectors for example)
-   Fix splitters not using the correct 'model' when copied or blueprinted
-   Fix issue where connected inserters not being copied when `shift + clicking` while not in build mode

### v2.0.1-Beta

-   Fix error where splitter cannot be pasted (`Too Close` error)

### v1.1.2

-   Fix incompatibility issue with CopyInserters where inserters were not being copied when in multibuild mode

### v1.1.1

-   Fix a bug where the build preview disappears and the game reports "building out of range", if, when in multibuild mode, only a single copy of the building is placed.

### v1.1.0

-   Store spacing indipendently per item so that you can place your power poles with a different spacing than your assembler without having to constantly tap `+` and `-`. You can opt out of this functionality and revert to the hold behavious by setting the `itemSpecificSpacing` config to `false`.
-   Add ability to use key `0` to reset spacing

### v1.0.2

-   Fixes incorrect (slightly large) spacing of accumulators

### v1.0.1

-   Fixes an null pointer error in some cases when the spacing was set to a value higher than the amount of spaces between start and end
-   Add ability to use keypad + and - to increase / decrease spacing
-   Fix spacing of power poles

### v1.0.0

-   Initial Release
