using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Kismet;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.BinaryConverters;

#nullable enable

namespace auto_patcher
{
    public class AutoPatcherLib
    {
        public static readonly int NumCpus = Environment.ProcessorCount;

        public const int LiaraInSquadPlotId = 6879;
        public const int LiaraWasInSquadPlotId = 10915;
        public const int LiaraInSquadPlotTransitionId = 10931;
        public const int LiaraWasInSquadPlotTransitionId = 10932;
        public const int LiaraHenchmanId = 14;
        public const int ModMountPriority = 10900;

        public static readonly ISet<string> ExcludedDirectories = new HashSet<string> {"DLC_MOD_LiaraSquad"};

        // these packages are modded manually
        public static readonly ISet<string> ExcludedPackages = new HashSet<string>
        {
            "BioH_SelectGUI.pcc",
            "BioP_Exp1Lvl2.pcc",
            "BioP_Exp1Lvl3.pcc",
            "BioP_Exp1Lvl4.pcc",
            "BioD_Unc1Base2_01Narrative_LOC_DEU.pcc",
            "BioD_Unc1Base2_01Narrative_LOC_FRA.pcc",
            "BioD_Unc1Base2_01Narrative_LOC_INT.pcc",
            "BioD_Unc1Base2_01Narrative_LOC_ITA.pcc",
            "BioD_Unc1Base2_01Narrative_LOC_POL.pcc",
            "BioD_EndGm1_310Huddle.pcc",
            "BioD_EndGm2_200Factory.pcc",
            "BioD_EndGm2_400FinalBattle.pcc",
            "BioD_EndGm2_410HoldTheLine.pcc",
            "BioD_EndGm2_430ReaperCombat.pcc",
            "BioD_EndGm2_440CutsceneRoll.pcc",
            "BioD_EndGm2_450ShepAlive.pcc"
        };

        public const string LiaraHenchTag = "hench_liara";

        public static readonly ISet<string> HenchTags = new HashSet<string>
        {
            "hench_vixen", // Miranda
            "hench_leading", // Jacob
            "hench_tali", // Tali
            "hench_professor", // Mordin
            "hench_mystic", // Samara / Morinth
            "hench_assassin", // Thane
            "hench_convict", // Jack
            "hench_thief", // Kasumi
            "hench_grunt", // Grunt
            "hench_geth", // Legion
            "hench_veteran", // Zaeed
            "hench_garrus", // Garrus
        };

        public static int GetDefaultBatchCount()
        {
            return Math.Min(8, Math.Max(1, NumCpus / 2));
        }

        public static readonly Dictionary<string, Func<ExportEntry, List<ExportEntry>, RelevantSequence?>>
            RelevantSequenceMap = new()
            {
                {"LookupHenchmanFromPlotManager", LookupHenchmanFromPlotManagerSequence.CreateIfRelevant},
                {"IsTag_Henchman", IsTagHenchmanSequence.CreateIfRelevant},
                {"Store_the_Henchmen_in_the_Squad", StoreTheHenchmenInTheSquadSequence.CreateIfRelevant},
                {
                    "Retrieve_the_Henchmen_previously_in_the_Squad",
                    RetrieveTheHenchmenPreviouslyInTheSquadSequence.CreateIfRelevant
                }
            };

        public static readonly Dictionary<string, IPackageHandler> PackageHandlers = new()
        {
            {"BioD_Nor", new BioDNorTriggerStreamsPackageHandler()},
            {"BioP_Nor", new BioPNorStreamingKismetPackageHandler()},
            {"BioD_Exp1Lvl5_100Stronghold", new StrongholdDeactivateLiaraPackageHandler()},
            {"BioD_Exp1Lvl4_Stage2_Out", new InsertShowMessageActionPackageHandler(234, 375, 84, 10900006)},
            {"BioD_Exp1Lvl5_200Cabin", new InsertShowMessageActionPackageHandler(566, 593, 554, 10900007)},
            {"BioH_Liara_00", new BioHLiaraSpawnSequencePackageHandler()},
            {"BioP_Global", new BioPGlobalPackageHandler()},
            {"BioP_EndGm1", new BioPEndGm1TriggerStreamsPackageHandler()},
            {"BioP_EndGm_StuntHench", new BioPEndGmStuntHenchPackageHandler()},
            {"BioD_EndGm2_300Conclusion", new BioDEndGm2300ConclusionPackageHandler()}
        };

        public static readonly Dictionary<int, int> ReplacedStateEventIds = new()
        {
            // In_Squad.Clear_Squad
            {75, 10934},
            // Was_In_Squad.Clear_all
            {1910, 10933}
        };

        private readonly IMessageReporter _messageReporter;
        private readonly IProgressEventListener? _progressEventListener;

        public AutoPatcherLib(IMessageReporter messageReporter, IProgressEventListener? progressEventListener = null)
        {
            _messageReporter = messageReporter;
            _progressEventListener = progressEventListener;
        }

        public void HandleSpecificFiles(IEnumerable<string> inputFiles, string outputDirName, bool adjustMountPriority)
        {
            var filteredInputFiles = inputFiles.Where(inputFile =>
            {
                if (!Path.HasExtension(inputFile) || !Path.GetExtension(inputFile).Equals(".pcc") ||
                    !File.Exists(inputFile))
                {
                    _messageReporter.ReportWarning($"File {inputFile} is not a PCC file");
                    return false;
                }

                return true;
            }).ToHashSet();

            var targetFiles =
                CollectAndCopyRelevantPackageFiles(filteredInputFiles, outputDirName, adjustMountPriority);
            HandleRelevantPackageFiles(targetFiles);
        }

        public void HandleGameDir(string gameDir, string outputDirName, int batchCount, bool adjustMountPriority)
        {
            if (!Directory.Exists(gameDir))
            {
                _messageReporter.ReportError($"Directory {gameDir} does not exist");
                return;
            }

            _progressEventListener?.OnScanning();

            var filePaths = new HashSet<string>();
            _messageReporter.ReportInformation("Finding package files");
            _progressEventListener?.OnStageStart("Finding package files");
            CollectPackageFiles(Path.GetFullPath(gameDir), filePaths);

            if (filePaths.IsEmpty())
            {
                _messageReporter.ReportError($"Could not find any package files in {gameDir} or any of its sub dirs");
                return;
            }

            _messageReporter.ReportInformation("Finding and copying relevant files");
            _progressEventListener?.OnStageStart("Finding and copying relevant files");
            var groupDictionary = new Dictionary<string, int>();
            var targetFiles = new ConcurrentBag<string>();
            BatchExecute(
                filePaths,
                batch =>
                {
                    var files = CollectAndCopyRelevantPackageFiles(batch, outputDirName, adjustMountPriority);
                    foreach (var file in files)
                    {
                        targetFiles.Add(file);
                    }
                },
                tuple =>
                {
                    var (file, idx) = tuple;
                    var fileName = Path.GetFileName(file);
                    // make sure the same packages end up in the same bucket
                    if (groupDictionary.TryGetValue(fileName, out var existingGroup))
                    {
                        return existingGroup;
                    }

                    return groupDictionary[fileName] = idx % batchCount;
                }
            );

            _progressEventListener?.OnStart(targetFiles.Count);
            _messageReporter.ReportInformation("Applying changes to files");
            _progressEventListener?.OnStageStart("Applying changes to files");

            BatchExecute(
                targetFiles,
                batchCount,
                HandleRelevantPackageFiles
            );

            _messageReporter.ReportInformation("Generating BioH_END_Liara_00");
            _progressEventListener?.OnStageStart("Generating BioH_END_Liara_00");
            GenerateBioHEndLiara(outputDirName);
            Console.WriteLine("Done");
        }

        void BatchExecute<T>(IEnumerable<T> data, int batchCount, Action<List<T>> action)
        {
            BatchExecute(data, action, tuple => tuple.Item2 % batchCount);
        }

        // Split data into batches according to the provided groupFunc and process them concurrently, awaiting the completion
        // for all items.
        void BatchExecute<T>(IEnumerable<T> data, Action<List<T>> action, Func<(T, int), int> groupFunc)
        {
            var batches = data
                .Select((item, idx) => (item, idx))
                .GroupBy(groupFunc.Invoke)
                .Select(group => @group.Select(item => item.item).ToList())
                .ToList();

            var countdownEvent = new CountdownEvent(1);
            foreach (var batch in batches)
            {
                countdownEvent.AddCount();
                Task.Run(() =>
                {
                    try
                    {
                        action.Invoke(batch);
                    }
                    catch (Exception e)
                    {
                        _messageReporter.ReportException(e, "Exception occurred executing submitted action");
                    }
                    finally
                    {
                        countdownEvent.Signal();
                    }
                });
            }

            countdownEvent.Signal();
            countdownEvent.Wait();
        }

        static void CollectPackageFiles(string dir, ISet<string> filePaths)
        {
            foreach (var subDir in Directory.GetDirectories(dir))
            {
                var dirName = new DirectoryInfo(subDir).Name;
                if (!ExcludedDirectories.Contains(dirName))
                {
                    CollectPackageFiles(subDir, filePaths);
                }
            }

            foreach (var file in Directory.GetFiles(dir))
            {
                if (".pcc".Equals(Path.GetExtension(file)))
                {
                    filePaths.Add(file);
                }
            }
        }

        IEnumerable<string> CollectAndCopyRelevantPackageFiles(
            IEnumerable<string> sourceFiles,
            string outputDir,
            bool adjustMountPriority
        )
        {
            List<string> targetFiles = new();
            // handle copies of packages in DLC files by mapping them by name and using the highest mount priority
            Dictionary<string, List<(int, string)>> packageMap = new();
            var currentMountPriority = ModMountPriority;
            var modMountFilePath = Path.Combine(outputDir, "Mount.dlc");
            MountFile? modMountFile;
            if (adjustMountPriority)
            {
                if (File.Exists(modMountFilePath))
                {
                    modMountFile = new MountFile(modMountFilePath);
                }
                else
                {
                    _messageReporter.ReportError($"Mount file path {modMountFilePath} does not exist.");
                    modMountFile = null;
                }
            }
            else
            {
                modMountFile = null;
            }

            foreach (var sourceFile in sourceFiles)
            {
                var sourceFileName = Path.GetFileName(sourceFile);

                using var fs = new FileStream(sourceFile, FileMode.Open, FileAccess.Read);
                using var package = MEPackageHandler.OpenMEPackageFromStream(fs, sourceFile);

                if (IsPackageRelevant(package))
                {
                    var directoryName = Path.GetDirectoryName(sourceFile);
                    if (ExcludedPackages.Contains(sourceFileName))
                    {
                        if (directoryName != null)
                        {
                            var directoryInfo = new DirectoryInfo(directoryName);
                            var parentName = directoryInfo.Parent?.Name;
                            if (parentName?.Contains("_MOD_") ?? false)
                            {
                                _messageReporter.ReportWarning(
                                    $"Directory {parentName} appears to be a mod and contains file {sourceFileName}, which cannot be patched by this tool. Compatibility is limited."
                                );
                                continue;
                            }
                        }

                        _messageReporter.ReportInformation($"Ignored excluded package {sourceFileName}");

                        continue;
                    }

                    var mountDlcFileName = Path.Combine(directoryName, "Mount.dlc");
                    int mountPriority;
                    if (File.Exists(mountDlcFileName))
                    {
                        mountPriority = new MountFile(mountDlcFileName).MountPriority;
                    }
                    else
                    {
                        mountPriority = 0;
                    }

                    if (mountPriority >= currentMountPriority)
                    {
                        if (modMountFile != null)
                        {
                            currentMountPriority = mountPriority + 1;
                            modMountFile.MountPriority = currentMountPriority;
                            modMountFile.WriteMountFile(modMountFilePath);
                        }
                        else
                        {
                            _messageReporter.ReportWarning(
                                $"Cannot override '{sourceFile}' since that directory's mount priority ({mountPriority}) is higher than {currentMountPriority}. Consider changing this mod's mount priority using Legendary Explorer's Mount Editor."
                            );
                        }
                    }

                    if (packageMap.ContainsKey(sourceFileName))
                    {
                        packageMap[sourceFileName].Add((mountPriority, sourceFile));
                    }
                    else
                    {
                        packageMap[sourceFileName] = new List<(int, string)> {(mountPriority, sourceFile)};
                    }
                }
            }

            foreach (var fileName in packageMap.Keys)
            {
                var relevantFileCopies = packageMap[fileName];

                string fileToUse;
                if (relevantFileCopies.Count == 1)
                {
                    fileToUse = relevantFileCopies[0].Item2;
                }
                else if (relevantFileCopies.Count > 1)
                {
                    fileToUse = relevantFileCopies.MaxBy(tuple => tuple.Item1).Item2;
                }
                else
                {
                    // sanity check, should never happen as the list is always initialised with one element
                    continue;
                }

                var targetFileName = Path.Combine(outputDir, fileName);
                File.Copy(fileToUse, targetFileName, true);
                targetFiles.Add(targetFileName);
                _messageReporter.ReportInformation($"Copied relevant file {fileName}");
            }

            return targetFiles;
        }

        void HandleRelevantPackageFiles(IEnumerable<string> targetFiles)
        {
            foreach (var targetFile in targetFiles)
            {
                var fileName = Path.GetFileName(targetFile);
                using var fs = new FileStream(targetFile, FileMode.Open, FileAccess.ReadWrite);
                using var package = MEPackageHandler.OpenMEPackageFromStream(fs, targetFile);
                fs.Close();

                void HandlePackage(IPackageHandler packageHandler, IMEPackage package)
                {
                    try
                    {
                        packageHandler.HandlePackage(package);
                        _messageReporter.ReportInformation($"Handled {packageHandler} in file {fileName}");
                    }
                    catch (SequenceStructureException e)
                    {
                        _messageReporter.ReportError(
                            $"Failed to handle {packageHandler} in file {fileName}: {e.Message}"
                        );
                    }
                    catch (Exception e)
                    {
                        _messageReporter.ReportException(
                            e,
                            $"Exception handling {packageHandler} in file {fileName}"
                        );
                    }
                }

                PackageHandlers.TryGetValue(package.FileNameNoExtension, out var handler);
                if (handler != null)
                {
                    HandlePackage(handler, package);
                }

                ScanPackage(package, true, out var collectedHandlers);

                foreach (var packageHandler in collectedHandlers)
                {
                    HandlePackage(packageHandler, package);
                }

                if (handler != null || !collectedHandlers.IsEmpty())
                {
                    package.Save();
                }
                else
                {
                    _messageReporter.ReportInformation($"File {targetFile} does not contain relevant sequences.");
                }

                _progressEventListener?.OnStepCompleted();
            }
        }

        static bool IsPackageRelevant(IMEPackage package)
        {
            return PackageHandlers.ContainsKey(package.FileNameNoExtension) || ScanPackage(package, false, out _);
        }

        static bool ScanPackage(IMEPackage package, bool collectHandlers, out List<IPackageHandler> collectedHandlers)
        {
            collectedHandlers = new List<IPackageHandler>();
            var foundLookupHenchmanFromPlotManager = false;

            foreach (var packageExport in package.Exports)
            {
                if ("InterpGroup".Equals(packageExport.ClassName))
                {
                    var weaponEquipInterpGroup = TrackGestureInterpGroup.CreateIfRelevant(package, packageExport);

                    if (weaponEquipInterpGroup != null)
                    {
                        if (!collectHandlers)
                        {
                            return true;
                        }

                        collectedHandlers.Add(weaponEquipInterpGroup);
                    }

                    continue;
                }

                if (!"Sequence".Equals(packageExport.ClassName))
                {
                    continue;
                }

                if (foundLookupHenchmanFromPlotManager)
                {
                    // the LookupHenchmanFromPlotManager is usually followed by 2 unnamed sequences that execute plot transitions
                    var plotTransitionSequence = PlotTransitionSequence.CreateIfRelevant(
                        packageExport,
                        SeqTools
                            .GetAllSequenceElements(packageExport)
                            .Select(entry => (ExportEntry) entry)
                            .ToList()
                    );

                    if (plotTransitionSequence != null)
                    {
                        if (!collectHandlers)
                        {
                            return true;
                        }

                        collectedHandlers.Add(plotTransitionSequence);
                        continue;
                    }
                }

                var objectName = packageExport.ObjectName.Name;

                if (objectName == null)
                {
                    continue;
                }

                var allSequenceElements = SeqTools.GetAllSequenceElements(packageExport);

                if (allSequenceElements == null
                    || !allSequenceElements.TrueForAll(sequenceElement => sequenceElement is ExportEntry))
                {
                    // skip sequences containing imported sequence objects
                    continue;
                }

                var sequenceObjects = allSequenceElements.Select(entry => (ExportEntry) entry).ToList();

                var replacedStateTransitions = sequenceObjects.Where(obj =>
                {
                    if (!"BioSeqAct_PMExecuteTransition".Equals(obj.ClassName)) return false;
                    var transitionId = obj.GetProperty<IntProperty>("m_nIndex");
                    return transitionId != null && ReplacedStateEventIds.ContainsKey(transitionId);
                }).ToList();

                if (!replacedStateTransitions.IsEmpty())
                {
                    if (!collectHandlers)
                    {
                        return true;
                    }

                    collectedHandlers.Add(
                        new ReplacedStateTransitionsSequence(
                            packageExport,
                            sequenceObjects,
                            packageExport,
                            replacedStateTransitions
                        )
                    );
                }

                RelevantSequenceMap.TryGetValue(objectName, out var relevantSequenceInitializer);

                // handle special cases
                if (relevantSequenceInitializer == null)
                {
                    // sometimes the Store_the_Henchmen_in_the_Squad sequence is unnamed
                    var storeTheHenchmenInTheSquadSequence =
                        StoreTheHenchmenInTheSquadSequence.GetFromUnnamedSequence(packageExport, sequenceObjects);
                    if (storeTheHenchmenInTheSquadSequence != null)
                    {
                        if (!collectHandlers)
                        {
                            return true;
                        }

                        collectedHandlers.Add(storeTheHenchmenInTheSquadSequence);
                    }
                    else
                    {
                        // Some sequences check for the gender of the hench, which based on hench_mystic (Samara / Morinth)
                        // counts asari as female. This sequence is very similar to the IsTag_Henchman sequence and can
                        // be handled the same using the CompareName sequence object for hench_mystic as base.
                        var henchmanGenderCheckSequence =
                            IsTagHenchmanSequence.FindHenchmanGenderCheckSequence(packageExport, sequenceObjects);
                        if (henchmanGenderCheckSequence != null)
                        {
                            if (!collectHandlers)
                            {
                                return true;
                            }

                            collectedHandlers.Add(henchmanGenderCheckSequence);
                        }
                    }

                    continue;
                }

                var relevantSequence = relevantSequenceInitializer.Invoke(
                    packageExport,
                    sequenceObjects
                );

                if (relevantSequence != null)
                {
                    if (!collectHandlers)
                    {
                        return true;
                    }

                    collectedHandlers.Add(relevantSequence);
                    foundLookupHenchmanFromPlotManager = objectName.Equals("LookupHenchmanFromPlotManager");
                }
            }

            return false;
        }

        public void GenerateBioHEndLiara(string outputDirName)
        {
            var sourceFileName = Path.Join(outputDirName, "BioH_Liara_00.pcc");
            if (!File.Exists(sourceFileName))
            {
                _messageReporter.ReportError("BioH_Liara_00 file not found in output dir");
                return;
            }

            var destFileName = Path.Join(outputDirName, "BioH_END_Liara_00.pcc");
            File.Copy(
                sourceFileName,
                destFileName,
                true
            );

            using var fs = new FileStream(destFileName, FileMode.Open, FileAccess.ReadWrite);
            using var package = MEPackageHandler.OpenMEPackageFromStream(fs, destFileName);
            fs.Close();

            var mainSequence = package.GetUExport(12908);
            var mainSequenceProps = mainSequence.GetProperties();

            var sequenceObjects = mainSequenceProps.GetProp<ArrayProperty<ObjectProperty>>("SequenceObjects");
            foreach (var sequenceObject in sequenceObjects)
            {
                EntryPruner.TrashEntryAndDescendants(sequenceObject.ResolveToEntry(package));
            }

            sequenceObjects.Clear();
            mainSequence.WriteProperties(mainSequenceProps);

            var levelLoaded = SequenceObjectCreator.CreateSequenceObject(package, "SeqEvent_LevelLoaded");
            KismetHelper.AddObjectToSequence(levelLoaded, mainSequence);

            CreateEndGmLiaraTeleportSequence(package, mainSequence, levelLoaded);

            package.FindNameOrAdd("RE_ENDGM_LOAD_LIARA");
            package.FindNameOrAdd("RE_ENDGM_LOADED_LIARA");
            package.FindNameOrAdd("RE_ENDGM_GETLOADED_STUNTHENCH");

            var endGmLoadLiaraEvent = SequenceObjectCreator.CreateSequenceObject(package, "SeqEvent_RemoteEvent");
            KismetHelper.AddObjectToSequence(endGmLoadLiaraEvent, mainSequence);
            var endGmLoadLiaraEventProps = endGmLoadLiaraEvent.GetProperties();
            endGmLoadLiaraEventProps.AddOrReplaceProp(new NameProperty("RE_ENDGM_LOAD_LIARA", "EventName"));
            endGmLoadLiaraEventProps.RemoveNamedProperty("VariableLinks");
            endGmLoadLiaraEvent.WriteProperties(endGmLoadLiaraEventProps);

            var liaraPawnSeqVar = SequenceObjectCreator.CreateSequenceObject(package, "SeqVar_Object");
            Util.WriteProperty<ObjectProperty>(
                liaraPawnSeqVar,
                "ObjValue",
                prop => prop.Value = 13088
            );
            KismetHelper.AddObjectToSequence(liaraPawnSeqVar, mainSequence);

            var activateEndGmLoadedLiara =
                SequenceObjectCreator.CreateSequenceObject(package, "SeqAct_ActivateRemoteEvent");
            KismetHelper.AddObjectToSequence(activateEndGmLoadedLiara, mainSequence);
            var activateEndGmLoadedLiaraProps = activateEndGmLoadedLiara.GetProperties();
            activateEndGmLoadedLiaraProps.AddOrReplaceProp(new NameProperty("RE_ENDGM_LOADED_LIARA", "EventName"));
            activateEndGmLoadedLiara.WriteProperties(activateEndGmLoadedLiaraProps);
            KismetHelper.CreateVariableLink(activateEndGmLoadedLiara, "Instigator", liaraPawnSeqVar);

            Util.AddLinkToOutputLink(levelLoaded, activateEndGmLoadedLiara, 0);
            Util.AddLinkToOutputLink(endGmLoadLiaraEvent, activateEndGmLoadedLiara, 0);

            var endGmGetLoadedStuntHench = SequenceObjectCreator.CreateSequenceObject(package, "SeqEvent_RemoteEvent");
            KismetHelper.AddObjectToSequence(endGmGetLoadedStuntHench, mainSequence);
            var endGmGetLoadedStuntHenchProps = endGmGetLoadedStuntHench.GetProperties();
            endGmGetLoadedStuntHenchProps.AddOrReplaceProp(new NameProperty("RE_ENDGM_GETLOADED_STUNTHENCH",
                "EventName"));
            endGmGetLoadedStuntHenchProps.RemoveNamedProperty("VariableLinks");
            endGmGetLoadedStuntHench.WriteProperties(endGmGetLoadedStuntHenchProps);

            var activateEndGmLoadedStuntHench =
                SequenceObjectCreator.CreateSequenceObject(package, "SeqAct_ActivateRemoteEvent");
            KismetHelper.AddObjectToSequence(activateEndGmLoadedStuntHench, mainSequence);
            var activateEndGmLoadedStuntHenchProps = activateEndGmLoadedStuntHench.GetProperties();
            activateEndGmLoadedStuntHenchProps.AddOrReplaceProp(new NameProperty("RE_ENDGM_LOADED_LIARA", "EventName"));
            activateEndGmLoadedStuntHench.WriteProperties(activateEndGmLoadedStuntHenchProps);
            KismetHelper.CreateVariableLink(activateEndGmLoadedStuntHench, "Instigator", liaraPawnSeqVar);

            Util.AddLinkToOutputLink(endGmGetLoadedStuntHench, activateEndGmLoadedStuntHench, 0);

            package.Save();
        }

        static void CreateEndGmLiaraTeleportSequence(
            IMEPackage package,
            ExportEntry mainSequence,
            ExportEntry levelLoadedEvent
        )
        {
            var checkStateInfiltrationComplete =
                SequenceObjectCreator.CreateSequenceObject(package, "BioSeqAct_PMCheckState");
            KismetHelper.AddObjectToSequence(checkStateInfiltrationComplete, mainSequence);
            KismetHelper.SetComment(checkStateInfiltrationComplete, "Infiltration Mission Completed?");
            var checkStateInfiltrationCompleteProps = checkStateInfiltrationComplete.GetProperties();
            checkStateInfiltrationCompleteProps.AddOrReplaceProp(new IntProperty(2941, new NameReference("m_nIndex")));
            checkStateInfiltrationCompleteProps.RemoveNamedProperty("VariableLinks");
            checkStateInfiltrationCompleteProps.RemoveNamedProperty("InputLinks");
            checkStateInfiltrationComplete.WriteProperties(checkStateInfiltrationCompleteProps);

            Util.AddLinkToOutputLink(levelLoadedEvent, checkStateInfiltrationComplete, 0);

            var checkStateLongWalkStarted = (ExportEntry) ((IEntry) checkStateInfiltrationComplete).Clone(true);
            KismetHelper.RemoveAllLinks(checkStateLongWalkStarted);
            package.AddExport(checkStateLongWalkStarted);
            KismetHelper.AddObjectToSequence(checkStateLongWalkStarted, mainSequence);
            KismetHelper.SetComment(checkStateLongWalkStarted, "Long Walk Mission Started?");
            Util.WriteProperty<IntProperty>(checkStateLongWalkStarted, "m_nIndex", prop => prop.Value = 2799);

            Util.AddLinkToOutputLink(checkStateInfiltrationComplete, checkStateLongWalkStarted, 0);

            var checkStateLongWalkFinished = (ExportEntry) ((IEntry) checkStateInfiltrationComplete).Clone(true);
            KismetHelper.RemoveAllLinks(checkStateLongWalkFinished);
            package.AddExport(checkStateLongWalkFinished);
            KismetHelper.AddObjectToSequence(checkStateLongWalkFinished, mainSequence);
            KismetHelper.SetComment(checkStateLongWalkFinished, "Long Walk Mission Completed?");
            Util.WriteProperty<IntProperty>(checkStateLongWalkFinished, "m_nIndex", prop => prop.Value = 2944);

            Util.AddLinkToOutputLink(checkStateLongWalkStarted, checkStateLongWalkFinished, 0);
            Util.AddLinkToOutputLink(checkStateInfiltrationComplete, checkStateLongWalkFinished, 1);

            var checkStateFinalBattleStarted = (ExportEntry) ((IEntry) checkStateInfiltrationComplete).Clone(true);
            KismetHelper.RemoveAllLinks(checkStateFinalBattleStarted);
            package.AddExport(checkStateFinalBattleStarted);
            KismetHelper.AddObjectToSequence(checkStateFinalBattleStarted, mainSequence);
            KismetHelper.SetComment(checkStateFinalBattleStarted, "Final Battle Mission Started?");
            Util.WriteProperty<IntProperty>(checkStateFinalBattleStarted, "m_nIndex", prop => prop.Value = 3055);

            Util.AddLinkToOutputLink(checkStateLongWalkFinished, checkStateFinalBattleStarted, 0);

            var teleportHuddle02 = SequenceObjectCreator.CreateSequenceObject(package, "SeqAct_Teleport");
            KismetHelper.AddObjectToSequence(teleportHuddle02, mainSequence);
            KismetHelper.SetComment(teleportHuddle02, "In endgm2 Huddle 02");

            var liaraPawnSeqVar = SequenceObjectCreator.CreateSequenceObject(package, "SeqVar_Object");
            Util.WriteProperty<ObjectProperty>(
                liaraPawnSeqVar,
                "ObjValue",
                prop => prop.Value = 13088
            );
            KismetHelper.AddObjectToSequence(liaraPawnSeqVar, mainSequence);

            KismetHelper.CreateVariableLink(teleportHuddle02, "Target", liaraPawnSeqVar);

            var wpFactoryLiara = SequenceObjectCreator.CreateSequenceObject(package, "BioSeqVar_ObjectFindByTag");
            KismetHelper.AddObjectToSequence(wpFactoryLiara, mainSequence);
            var wpFactoryLiaraProps = wpFactoryLiara.GetProperties();
            wpFactoryLiaraProps.AddOrReplaceProp(new StrProperty("wp_Factory_Liara",
                new NameReference("m_sObjectTagToFind")));
            wpFactoryLiara.WriteProperties(wpFactoryLiaraProps);

            KismetHelper.CreateVariableLink(teleportHuddle02, "Destination", wpFactoryLiara);

            Util.AddLinkToOutputLink(checkStateLongWalkStarted, teleportHuddle02, 1);

            var wpFinalLiara = SequenceObjectCreator.CreateSequenceObject(package, "BioSeqVar_ObjectFindByTag");
            KismetHelper.AddObjectToSequence(wpFinalLiara, mainSequence);
            var wpFinalLiaraProps = wpFinalLiara.GetProperties();
            wpFinalLiaraProps.AddOrReplaceProp(new StrProperty("wp_Final_Liara",
                new NameReference("m_sObjectTagToFind")));
            wpFinalLiara.WriteProperties(wpFinalLiaraProps);

            var teleportHuddle03 = SequenceObjectCreator.CreateSequenceObject(package, "SeqAct_Teleport");
            KismetHelper.AddObjectToSequence(teleportHuddle03, mainSequence);
            KismetHelper.SetComment(teleportHuddle03, "In endgm2 Huddle 03");
            KismetHelper.CreateVariableLink(teleportHuddle03, "Target", liaraPawnSeqVar);
            KismetHelper.CreateVariableLink(teleportHuddle03, "Destination", wpFinalLiara);
            Util.AddLinkToOutputLink(checkStateFinalBattleStarted, teleportHuddle03, 1);
        }
    }

    public interface IMessageReporter
    {
        void ReportException(Exception e, string msg);

        void ReportError(string msg);

        void ReportWarning(string msg);

        void ReportInformation(string msg);
    }

    public class ConsoleReporter : IMessageReporter
    {
        private readonly bool Verbose;

        public ConsoleReporter(bool verbose)
        {
            Verbose = verbose;
        }

        public void ReportException(Exception e, string msg)
        {
            Console.WriteLine($"ERROR: {msg}: {e}");
        }

        public void ReportError(string msg)
        {
            Console.WriteLine($"ERROR: {msg}");
        }

        public void ReportWarning(string msg)
        {
            Console.WriteLine($"WARN: {msg}");
        }

        public void ReportInformation(string msg)
        {
            if (Verbose)
            {
                Console.WriteLine($"INFO: {msg}");
            }
        }
    }

    public interface IProgressEventListener
    {
        public void OnScanning();

        public void OnStart(int itemCount);

        public void OnStageStart(string stageName);

        public void OnStepCompleted();
    }

    // thrown when the sequence does match the expected structure, e.g. if output links or variable links are missing
    class SequenceStructureException : Exception
    {
        public SequenceStructureException(string message) : base(message)
        {
        }
    }
}