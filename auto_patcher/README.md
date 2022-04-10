Auto patch CLI utility that applies all changes to relevant sequences automatically, except for certain files that require
special adjustments and have to be modded manually (e.g. the LotSB DLC).

Options:

| Option                    | Description |
|---------------------------|---------------------------------------------------------------------------------------------|
| -f, --files               | Specific package files to mod, must be set if -g is not set. |
| -g, --gamedir             | Game directory to scan for package files, must be set if -f is not set. |
| -d, --dir                 | The output directory for modified packages, defaults to '..\DLC_MOD_LiaraSquad\CookedPCConsole'. |
| -v, --verbose             | Set output to verbose messages. |
| --bioh-end-only           | Only generate the BioH_END_Liara_00 file based on the output dir's BioH_Liara_00 file. Ignores -f and -g. |
| -b, --batch-count         | The number of batches files are split into for concurrent execution. The actual number of threads is defined by .net's default thread pool configuration. Only applies to handling the entire game directory using -g. |
| --retain-mount-priority   | Prevent auto_patcher from adjusting the mount priority of this mod automatically to ensure it mounts above mods where files have been patched. |
| --help                    | Display this help screen. |
| --version                 | Display version information. |

## Run

To run the patcher open its installation directory in a terminal and enter the following command:

Mod the whole game: `auto_patcher.exe -g "<game dir>" -v -d "<mod dir>"`

- replace `<game dir>` with the location of the LE2 install, e.g. `E:\SteamLibrary\steamapps\common\Mass Effect Legendary Edition\Game\ME2\BioGame"`
- and `<mod dir>` with the location of this mod's `CookedPCConsole` directory, e.g. `C:\Users\username\Documents\ME3TweaksModManager\mods\LE2\LE2-Liara_Squadmate\DLC_MOD_LiaraSquad\CookedPCConsole`

Mod a single file: `auto_patcher.exe -f "<file location>" -v -d "<target dir>""`

- replace `<file location>` with the path to the file to mod
- and `<target dir>` with the target directory for the resulting file

## Dependencies

References the LegendaryExplorerCore library from the LegendaryExplorer submodule, which takes care of loading required
DLLs at runtime.
