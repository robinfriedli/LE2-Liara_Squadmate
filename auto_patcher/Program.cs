#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandLine;
using LegendaryExplorerCore.GameFilesystem;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Kismet;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;
using LegendaryExplorerCore.Unreal.ObjectInfo;

namespace auto_patcher
{
    class Program
    {
        public const int LiaraInSquadPlotId = 6879;
        public const int LiaraWasInSquadPlotId = 10900105;
        public const int LiaraInSquadPlotTransitionId = 10900301;
        public const int LiaraWasInSquadPlotTransitionId = 10900302;
        public const int LiaraHenchmanId = 14;

        const string ModPackageDir = "..\\DLC_MOD_LiaraSquad\\CookedPCConsole";

        private static readonly ISet<string> ExcludedDirectories = new HashSet<string> {"DLC_MOD_LiaraSquad"};

        // these packages are modded manually
        private static readonly ISet<string> ExcludedPackages = new HashSet<string>
        {
            "BioP_Global.pcc",
            "BioH_SelectGUI.pcc",
            "BioP_Exp1Lvl2.pcc",
            "BioP_Exp1Lvl3.pcc",
            "BioP_Exp1Lvl4.pcc",
            "BioH_Liara_00.pcc",
            "BioD_Unc1Base2_01Narrative_LOC_DEU.pcc",
            "BioD_Unc1Base2_01Narrative_LOC_FRA.pcc",
            "BioD_Unc1Base2_01Narrative_LOC_INT.pcc",
            "BioD_Unc1Base2_01Narrative_LOC_ITA.pcc",
            "BioD_Unc1Base2_01Narrative_LOC_POL.pcc"
        };

        private static readonly Dictionary<string, Func<ExportEntry, List<ExportEntry>, RelevantSequence?>>
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

        public static readonly Dictionary<int, int> ReplacedStateEventIds = new()
        {
            // In_Squad.Clear_Squad
            {75, 10900304},
            // Was_In_Squad.Clear_all
            {1910, 10900303}
        };

        public class Options
        {
            [Option('f', "files",
                HelpText = "Specific package files to mod, must be set if -g is not set.")]
            public IEnumerable<string>? InputFiles { get; set; }

            [Option('g', "gamedir",
                HelpText = "Game directory to scan for package files, must be set if -f is not set.")]
            public string? GameDir { get; set; }

            [Option('d', "dir",
                HelpText =
                    "The output directory for modified packages, defaults to '..\\DLC_MOD_LiaraSquad\\CookedPCConsole'.")]
            public string? OutputDir { get; set; }

            [Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages.")]
            public bool Verbose { get; set; }
        }

        static void Main(string[] args)
        {
            Parser
                .Default
                .ParseArguments<Options>(args)
                .WithParsed(RunWithOptions);
        }

        static void InitLegendaryExplorer()
        {
            MEPackageHandler.Initialize();
            LE2UnrealObjectInfo.loadfromJSON();
            PackageSaver.Initialize();
            PackageSaver.PackageSaveFailedCallback = s => { Console.WriteLine($"ERROR: Failed to save package, {s}"); };
        }

        static void RunWithOptions(Options options)
        {
            InitLegendaryExplorer();

            var outputDirName = options.OutputDir ?? ModPackageDir;
            if (!Directory.Exists(outputDirName))
            {
                Directory.CreateDirectory(outputDirName);
                if (options.Verbose)
                {
                    Console.WriteLine($"INFO: Created new output dir {outputDirName}");
                }
            }

            if (options.InputFiles != null && !options.InputFiles.IsEmpty())
            {
                HandleSpecificFiles(options.InputFiles, options.Verbose, outputDirName);
            }
            else if (options.GameDir != null && !options.GameDir.IsEmpty())
            {
                HandleGameDir(options.GameDir, outputDirName, options.Verbose);
            }
            else
            {
                Console.WriteLine("Either -g (game dir) or -f (specific files) must be set.");
            }
        }

        static void HandleSpecificFiles(IEnumerable<string> inputFiles, bool verbose, string outputDirName)
        {
            var filteredInputFiles = inputFiles.Where(inputFile =>
            {
                if (!Path.HasExtension(inputFile) || !Path.GetExtension(inputFile).Equals(".pcc") ||
                    !File.Exists(inputFile))
                {
                    Console.WriteLine($"WARN: File {inputFile} is not a PCC file");
                    return false;
                }

                return true;
            }).ToHashSet();

            var targetFiles = CollectAndCopyRelevantPackageFiles(filteredInputFiles, outputDirName);
            HandleRelevantPackageFiles(targetFiles, verbose);
        }

        static void HandleGameDir(string gameDir, string outputDirName, bool verbose)
        {
            if (!Directory.Exists(gameDir))
            {
                Console.WriteLine($"Directory {gameDir} does not exist");
                return;
            }

            var filePaths = new HashSet<string>();
            Console.WriteLine("INFO: Finding package files");
            CollectPackageFiles(Path.GetFullPath(gameDir), filePaths);

            if (filePaths.IsEmpty())
            {
                Console.WriteLine($"Could not find any package files in {gameDir} or any of its sub dirs");
                return;
            }

            Console.WriteLine("INFO: Finding and copying relevant files");
            var targetFiles = CollectAndCopyRelevantPackageFiles(filePaths, outputDirName);
            Console.WriteLine("INFO: Applying changes to files");
            HandleRelevantPackageFiles(targetFiles, verbose);
            Console.WriteLine("Done");
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

        static IEnumerable<string> CollectAndCopyRelevantPackageFiles(IEnumerable<string> sourceFiles, string outputDir)
        {
            List<string> targetFiles = new();
            // handle copies of packages in DLC files by mapping them by name and using the highest mount priority
            Dictionary<string, List<(int, string)>> packageMap = new();

            foreach (var sourceFile in sourceFiles)
            {
                var sourceFileName = Path.GetFileName(sourceFile);

                if (ExcludedPackages.Contains(sourceFileName))
                {
                    Console.WriteLine($"INFO: Ignored excluded package {sourceFileName}");
                    continue;
                }

                using var fs = new FileStream(sourceFile, FileMode.Open, FileAccess.Read);
                var package = MEPackageHandler.OpenMEPackageFromStream(fs, sourceFile);

                if (IsPackageRelevant(package))
                {
                    var mountDlcFileName = Path.GetDirectoryName(sourceFile) + "\\Mount.dlc";
                    int mountPriority;
                    if (File.Exists(mountDlcFileName))
                    {
                        mountPriority = new MountFile(mountDlcFileName).MountPriority;
                    }
                    else
                    {
                        mountPriority = 0;
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
            }

            return targetFiles;
        }

        static void HandleRelevantPackageFiles(IEnumerable<string> targetFiles, bool verbose)
        {
            foreach (var targetFile in targetFiles)
            {
                var fs = new FileStream(targetFile, FileMode.Open, FileAccess.ReadWrite);
                var package = MEPackageHandler.OpenMEPackageFromStream(fs, targetFile);
                fs.Close();

                var relevantSequences = CollectRelevantSequences(package);

                if (!relevantSequences.IsEmpty())
                {
                    foreach (var relevantSequence in relevantSequences)
                    {
                        try
                        {
                            relevantSequence.HandleSequence(package);
                            if (verbose)
                            {
                                Console.WriteLine(
                                    $"INFO: Handled sequence {relevantSequence.Sequence.ObjectName.Name} in file {targetFile}");
                            }
                        }
                        catch (SequenceStructureException e)
                        {
                            Console.WriteLine(
                                $"ERROR: Failed to handle sequence {relevantSequence.Sequence.ObjectName.Name} in file {targetFile}: {e.Message}");
                        }
                    }

                    package.Save();
                }
                else if (verbose)
                {
                    Console.WriteLine($"INFO: File {targetFile} does not contain relevant sequences.");
                }
            }
        }

        static bool IsPackageRelevant(IMEPackage package)
        {
            return !CollectRelevantSequences(package).IsEmpty();
        }

        static List<RelevantSequence> CollectRelevantSequences(IMEPackage package)
        {
            var relevantSequences = new List<RelevantSequence>();
            var foundLookupHenchmanFromPlotManager = false;

            foreach (var packageExport in package.Exports)
            {
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
                        relevantSequences.Add(plotTransitionSequence);
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
                    relevantSequences.Add(
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
                        relevantSequences.Add(storeTheHenchmenInTheSquadSequence);
                    }

                    continue;
                }

                var relevantSequence = relevantSequenceInitializer.Invoke(
                    packageExport,
                    sequenceObjects
                );

                if (relevantSequence != null)
                {
                    relevantSequences.Add(relevantSequence);
                    foundLookupHenchmanFromPlotManager = objectName.Equals("LookupHenchmanFromPlotManager");
                }
            }

            return relevantSequences;
        }
    }

    // thrown when the sequence does match the expected structure, e.g. if output links or variable links are missing
    class SequenceStructureException : Exception
    {
        public SequenceStructureException(string message) : base(message)
        {
        }
    }
}