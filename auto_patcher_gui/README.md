GUI for the auto_patcher pool that invokes the common auto_patcher library with the following options:

 - LE2 Game Location: Location of the LE2 installation's BioGame directory.
 - Mod Import Location: Location of this mod's CookedPCConsole directory within the ME3Tweaks mod library.
 - Batch Count: Number of batches the relevant files are split into for concurrent execution. A higher number means higher resource (CPU / Memory) usage. Defaults to number of virtual CPUs / 2 or 8 maximum.
 - Adjust Mod Mount Priority Automatically: Whether to adjust this mod's mount priority automatically to ensure it is mounted above other mods where files have been patched.
