## [2.0] - 2022-04-10

 - Added Liara to the Normandy crew after completing the Normandy date.
   - Added level BioD_Nor_109Liara that places Liara at an unused terminal at the CIC and add greeting from ME3.
 - Added text prompts when Liara is recruited or moved to the Normandy.
 - Handle additional cutscenes by handling all InterpGroups with an InterpTrack for each squadmate by adding a track for Liara based on Samara.
   - Fixes Liara standing in the shuttle instead of sitting during the opening cutscene for Jack's loyalty mission, and presumably more cutscenes.
 - Expanded auto_patcher:
   - Added a GUI desktop app for the auto_patcher: auto_patcher_gui.
   - Handle additional packages automatically that were previously modded manually.
   - Speed up process by splitting the files into a configurable number of batches that are handled concurrently.
 - Adjust suicide mission:
   - Fix cutscene after the long walk / biotic specialist phase when Liara and Miranda are in the squad and make sure that the success cutscene plays as neither of them can die at this point.
   - Adjust cutscene after the reaper battle to reverse the roles of the squadmate that is rescued first and the squadmate that rescues Shepard, setting both to Liara if in squad.

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
