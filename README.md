# LE2 Liara squad mate

Mod that adds Liara from Lair of the Shadow Broker as a selectable squad mate to Mass Effect 2 in the Mass Effect Legendary Edition.
Uses LotSB both as recruitment and loyalty mission and enables Liara to be selected from the squad selection screen
like any other ME2 squad mate, this includes the loadout selection and power level-up screen. As this uses Liara as
defined by LotSB it matches the DLC both in appearance and gameplay in terms of what loadouts and powers are available
and how they function.

## WIP - Checklist

 - [X] Add Liara to Squad Selection screen by modding `GUI_SF_TeamSelect.TeamSelect` GFx GUI in `BioH_SelectGUI.pcc`
   - [X] Expand `m_lstLoadouts` in `SFXGameContent.Default__BioSeqAct_ShowPartySelectionGUI` to enable loadout info for Liara
   - [X] Add portrait textures from the ME3 Squad Selection screen
 - [X] Implement plot handling for Liara
   - [X] Add Liara to `lstMemberInfo` in `SFXGame.BioSFHandler_PartySelection` in `BIOUI.ini` and introduce new tags (e.g. AvailableLabel=IsSelectableLiara)
   - [X] Map these new tags to plot variables by extending the `PlotManagerGameData` Bio2DA lookup table (see plot variables section)
 - [X] Add Liara to `HenchmanPackageMap` under `SFXGame.SFXGame` in `BIOGame.ini`
 - [X] Expand global squad spawn sequence in `BioP_Global.pcc` for Liara
   - [X] Expand `Create_Henchmen` sequences by adding sequence for label "Liara", which invokes `SetStreamingState` with "stream_liara_00"
   - [X] Add 15th output link to switch in `SpawnHenchman_1` and `SpawnHenchman_0` invoking `Create_Henchmen` sequence with label "Liara" for ID 14
 - [X] Adjust certain mission specific squad selection sequences in BioP files
   - [X] Mod sequence `LookupHenchmanFromPlotManager` to look up plot flag 6879 (InSquadLiara) and set Henchman ID to 14 if true
   - [X] Mod following 2 sequences after `LookupHenchmanFromPlotManager` to compare int to 15 instead of 14 and add 15th output link to switch, which invokes transition 10900301 setting plot bool 6879 to true
   - [X] Add `CompareName` sequence for "hench_liara" to all `IsTag_Henchman` sequences
 - [X] Adjust certain cutscenes in BioD files
   - [X] Add `CompareName` sequence for "hench_liara" to all `IsTag_Henchman` sequences
 - [X] Patch LotSB to fix incompatibilities with this mod's global hanling for Liara
   - [X] Mirror changes to `GUI_SF_TeamSelect` from `BioH_SelectGUI`
   - [X] Remove special handling for Liara from sequence `REF_Henchmen_PlaceOnceInMasterMap` and adjust `LookupHenchmanFromPlotManager` sequence analogous to rest of game to handle Liara globally instead
 - [ ] Patch Suicide Mission to enable support for Liara
 - [X] Write auto patch tool to adjust remaining BioP and BioD files automatically
 - [ ] Polish Squad Selection screen
   - [ ] Fix visuals for highlight effect when Liara is selected (currently showing double)

All auto modded files created by auto_patcher have been published, so all missions and spawns should work. However, most
missions are still untested and the suicide mission has not been handled yet.

## Mod info

The mod adds DLC `DLC_MOD_LiaraSquad` with mount priority 10900 and uses values in the range of 10900XXX for any new
plot variables, transitions, coniditionals etc.

## Plot variables

| Tag                | Plot variable | Desc                      |
| ------------------ | ------------- | ------------------------- |
| InSquadLiara       | 6879          | LiaraInSquad (LotSB)      |
| InPartyLiara       | 6961          | Started_Car_Chase (LotSB) |
| IsSelectableLiara  | 6951          | End_Mission (LotSB)       |
| IsLoyalLiara       | 6951          | End_Mission (LotSB)       |
| IsSpecializedLiara | 10900102      | (new plot bool)           |
| WasLoyalLiara      | 10900103      | (new plot bool)           |
| KnowExistLiara     | 6961          | Started_Car_Chase (LotSB) |
| AppearanceLiara    | 10900104      | (new plot int, unused)    |
| WasInSquadLiara    | 10900105      | (new plot bool)           |

Started_Car_Chase is used for InPartyLiara and KnowExistLiara to make sure Liara appears in the squad selection screen
during LotSB (there is a squad selection screen before reaching the Shadow Broker ship). Note that IsSelectableLiara is
enabled later by 6951 (End_Mission), which means after LotSB has been completed, guaranteeing that Liara cannot be deselected
during LotSB, similar to how loyalty missions work.

## Plot transitions / state events

| State Event | Desc                                                          |
| ----------- | ------------------------------------------------------------- |
| 10900301    | Set 6879 (LiaraInSquad) to true                               |
| 10900302    | Set 10900105 (WasInSquadLiara) to true                        |
| 10900303    | Expansion of 1910 (Was_In_Squad.Clear_all) that handles Liara |
| 10900304    | Expansion of 75 (In_Squad.Clear_Squad) that handles Liara     |
