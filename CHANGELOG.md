## [1.3] - 2022-01-19

 - Added compatibility patch for the Expanded Shepard Armory mod.
 - Added compatibility patch for the Wrex Armour Consistency mod.
 - Expanded auto_patcher to handle InterpGroup objects that equip weapons.
   - Handle InterpGroup objects that have an InterpTrack for each squadmate by creating a new InterTrack based on hench_mystic.
   - Fixes Liara not holding a weapon in the Niket confrontation cutscene during Miranda's loyalty mission.

## [1.2] - 2022-01-11

 - Added compatibility patch for the Liara Consistency Project LE2 DLC mod.
 - Added banner image.

## [1.1] - 2021-11-29

 - Increment plot int 22 (Party_Size) for Liara.
   - Add merge_mod that modifies function BioSeqAct_SetPlotPartySize.Activated to increment nPartyCount if plot bool 6961 (InPartyLiara / Started_Car_Chase (LotSB)) is set.
 - Polish squad selection GUI in GUI_SF_TeamSelect.TeamSelect.
   - Fix aspect ratio of scaleX and scaleY.
   - Improve positioning of selected, unselected and silhouette image in select and deselect animations.
 - Change plot variable and transition IDs to use range 109XX instead of 10900XXX.
   - Note that as a side effect this will cause the animation played when gaining Liara's loyalty to play again next time the squad selection screen is opened.
 - Add Liara to Determine_Player_Ending sequence in BioD_EndGm2_440CutsceneRoll.pcc.
   - Only one other squadmate is required to survive to not get the bad ending if Liara is in the party.
 - Refactor auto_patcher to use LegendaryExplorer as git submodule.
 - Use LegendaryExplorerCoreLib#InitLib to initialise library, which takes care of loading DLLs required at runtime.
