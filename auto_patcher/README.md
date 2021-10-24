Auto patch CLI utility that applies all changes to relevant sequences automatically, except for certain files that require
special adjustments and have to be modded manually (e.g. the LotSB DLC).

Options:
-f, --files      Specific package files to mod, must be set if -g is not set.

-g, --gamedir    Game directory to scan for package files, must be set if -f is not set.

-d, --dir        The output directory for modified packages, defaults to '..\DLC_MOD_LiaraSquad\CookedPCConsole'.

-v, --verbose    Set output to verbose messages.

--help           Display this help screen.

--version        Display version information.

## Compile time dependencies

Needs to link with the LegendaryExplorerCore.dll library for compilation. The file is expected to be found in
`..\..\..\..\data\ExternalTools\Legendary Explorer (Nightly)\LegendaryExplorerCore.dll` relative to the mod directory,
as the mod is expected to be installed in `ME3TweaksModManager\mods\LE2`, where it should be located if
Legendary Explorer (Nightly) is installed (can be installed from the ME3Tweaks Mod Manager Tools menu). Other locations
can be configured by changing the `<HintPath>` in `auto_patcher.csproj`.

## Runtime dependencies

For the program to run successfully the dynamic libraries in e.g.
`ME3TweaksModManager\data\ExternalTools\Legendary Explorer (Nightly)\runtimes\win-x64\native` need to be included in
the environment's path.
