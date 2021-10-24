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
        public const int LiaraInSquadPlotTransitionId = 10900301;
        public const int LiaraHenchmanId = 14;

        const string ModPackageDir = "..\\DLC_MOD_LiaraSquad\\CookedPCConsole";

        private static readonly ISet<string> ExcludedDirectories = new HashSet<string> {"DLC_MOD_LiaraSquad"};

        // these packages are modded manually
        private static readonly ISet<string> ExcludedPackages = new HashSet<string>
        {
            "BioP_Global.pcc",
            "BioP_Exp1Lvl2.pcc",
            "BioP_Exp1Lvl3.pcc",
            "BioP_Exp1Lvl4.pcc"
        };

        private static readonly Dictionary<string, Func<ExportEntry, List<ExportEntry>, RelevantSequence?>>
            RelevantSequenceMap = new()
            {
                {"LookupHenchmanFromPlotManager", LookupHenchmanFromPlotManagerSequence.CreateIfRelevant},
                {"IsTag_Henchman", IsTagHenchmanSequence.CreateIfRelevant}
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
                if (foundLookupHenchmanFromPlotManager)
                {
                    // the LookupHenchmanFromPlotManager is usually followed by 2 unnamed sequences that execute plot transitions
                    if ("Sequence".Equals(packageExport.ClassName))
                    {
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
                }

                var objectName = packageExport.ObjectName.Name;

                if (objectName == null)
                {
                    continue;
                }

                RelevantSequenceMap.TryGetValue(objectName, out var relevantSequenceInitializer);

                if (relevantSequenceInitializer == null)
                {
                    continue;
                }

                var allSequenceElements = SeqTools.GetAllSequenceElements(packageExport);

                if (!allSequenceElements.TrueForAll(sequenceElement => sequenceElement is ExportEntry))
                {
                    // skip sequences containing imported sequence objects
                    continue;
                }

                var relevantSequence = relevantSequenceInitializer.Invoke(
                    packageExport,
                    allSequenceElements.Select(entry => (ExportEntry) entry).ToList()
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

    abstract class RelevantSequence
    {
        public ExportEntry Sequence { get; set; }
        public List<ExportEntry> SequenceObjects { get; set; }
        public ExportEntry KeySequenceObject { get; set; }

        public RelevantSequence(ExportEntry sequence, List<ExportEntry> sequenceObjects, ExportEntry keySequenceObject)
        {
            Sequence = sequence;
            SequenceObjects = sequenceObjects;
            KeySequenceObject = keySequenceObject;
        }

        public abstract void HandleSequence(IMEPackage package);
    }

    class LookupHenchmanFromPlotManagerSequence : RelevantSequence
    {
        public LookupHenchmanFromPlotManagerSequence(ExportEntry sequence, List<ExportEntry> sequenceObjects,
            ExportEntry keySequenceObject) : base(sequence, sequenceObjects, keySequenceObject)
        {
        }

        public static LookupHenchmanFromPlotManagerSequence? CreateIfRelevant(ExportEntry sequence,
            List<ExportEntry> sequenceObjects)
        {
            var keySequenceObject = sequenceObjects.FindLast(sequenceObject =>
                sequenceObject.ClassName.Equals("BioSeqAct_PMCheckState"));
            return keySequenceObject != null
                ? new LookupHenchmanFromPlotManagerSequence(sequence, sequenceObjects, keySequenceObject)
                : null;
        }

        public override void HandleSequence(IMEPackage package)
        {
            // copy PMCheckState for Liara

            var outboundLinksOfNode = SeqTools.GetOutboundLinksOfNode(KeySequenceObject);
            var checkStateLiara = (ExportEntry) ((IEntry) KeySequenceObject).Clone(true);
            KismetHelper.SetComment(checkStateLiara, "Liara");
            package.AddExport(checkStateLiara);

            var falseOutputLink = Util.GetOutboundLink(outboundLinksOfNode, 1, 0);
            falseOutputLink.LinkedOp = checkStateLiara;
            SeqTools.WriteOutboundLinksToNode(KeySequenceObject, outboundLinksOfNode);

            Util.WriteProperty<IntProperty>(
                checkStateLiara,
                "m_nIndex",
                prop => prop.Value = Program.LiaraInSquadPlotId
            );

            KismetHelper.AddObjectToSequence(checkStateLiara, Sequence);

            // copy SequenceReference for Liara

            var trueOutputLink = Util.GetOutboundLink(outboundLinksOfNode, 0, 0);

            var trueLinkedOp = trueOutputLink.LinkedOp;
            if (trueLinkedOp is not ExportEntry)
            {
                throw new SequenceStructureException("CheckState true output does not link to ExportEntry");
            }

            // get SequenceReference of KeySequenceObject and clone for Liara
            var keySequenceReference = (ExportEntry) trueLinkedOp;
            var liaraSequenceReference = (ExportEntry) trueLinkedOp.Clone(true);
            package.AddExport(liaraSequenceReference);
            KismetHelper.AddObjectToSequence(liaraSequenceReference, Sequence);

            // relink SequenceReference of KeySequenceObject to the CheckState object for Liara
            var keySequenceReferenceOutputLinks = SeqTools.GetOutboundLinksOfNode(keySequenceReference);
            var keySequenceReferenceOutLink = Util.GetOutboundLink(keySequenceReferenceOutputLinks, 0, 0);
            keySequenceReferenceOutLink.LinkedOp = checkStateLiara;
            SeqTools.WriteOutboundLinksToNode(keySequenceReference, keySequenceReferenceOutputLinks);

            // link CheckState object Liara to SequenceObject for Liara if true
            var checkStateLiaraOutboundLinks = SeqTools.GetOutboundLinksOfNode(checkStateLiara);
            var checkStateLiaraTrueLink = Util.GetOutboundLink(checkStateLiaraOutboundLinks, 0, 0);
            checkStateLiaraTrueLink.LinkedOp = liaraSequenceReference;
            SeqTools.WriteOutboundLinksToNode(checkStateLiara, checkStateLiaraOutboundLinks);

            // copy referenced sequence and relink SequenceReference for Liara
            var keyReferencedSequenceProperty = keySequenceReference.GetProperty<ObjectProperty>("oSequenceReference");
            if (keyReferencedSequenceProperty == null)
            {
                throw new SequenceStructureException("ReferenceSequence does not reference any sequence");
            }

            var keyReferencedSequence = package.GetUExport(keyReferencedSequenceProperty.Value);
            var liaraReferencedSequence = (ExportEntry) ((IEntry) keyReferencedSequence).Clone(true);

            var liaraReferenceSequenceObjects = SeqTools
                .GetAllSequenceElements(liaraReferencedSequence)
                .Select(entry => (ExportEntry) entry)
                .ToList();

            Util.WriteProperty<ArrayProperty<ObjectProperty>>(
                liaraReferencedSequence,
                "SequenceObjects",
                prop => prop.Clear()
            );

            package.AddExport(liaraReferencedSequence);
            Util.CopyAllSequenceObjects(liaraReferenceSequenceObjects, liaraReferencedSequence, package);
            liaraReferencedSequence.idxLink = liaraSequenceReference.UIndex;

            Util.WriteProperty<ObjectProperty>(
                liaraSequenceReference,
                "oSequenceReference",
                prop => prop.Value = liaraReferencedSequence.UIndex
            );

            Util.WriteProperty<ObjectProperty>(
                liaraReferencedSequence,
                "ParentSequence",
                prop => prop.Value = liaraSequenceReference.UIndex
            );

            var newLiaraReferenceSequenceObjects = SeqTools
                .GetAllSequenceElements(liaraReferencedSequence)
                .Select(entry => (ExportEntry) entry)
                .ToList();

            var liaraReferencedSequenceActivated =
                newLiaraReferenceSequenceObjects.Find(obj => "SeqEvent_SequenceActivated".Equals(obj.ClassName));
            var liaraReferencedFinishSequence =
                newLiaraReferenceSequenceObjects.Find(obj => "SeqAct_FinishSequence".Equals(obj.ClassName));
            if (liaraReferencedSequenceActivated == null || liaraReferencedFinishSequence == null)
            {
                throw new SequenceStructureException(
                    "Referenced sequence does not have SequenceActivated or FinishSequence objects");
            }

            var liaraReferencedSequenceProps = liaraReferencedSequence.GetProperties();
            var inputLinksProp = liaraReferencedSequenceProps.GetProp<ArrayProperty<StructProperty>>("InputLinks");
            var liaraReferencedSequenceLinkedInputOp = inputLinksProp[0].GetProp<ObjectProperty>("LinkedOp");
            liaraReferencedSequenceLinkedInputOp.Value = liaraReferencedSequenceActivated.UIndex;
            var outputLinksProp = liaraReferencedSequenceProps.GetProp<ArrayProperty<StructProperty>>("OutputLinks");
            var liaraReferencedSequenceLinkedOutputOp = outputLinksProp[0].GetProp<ObjectProperty>("LinkedOp");
            liaraReferencedSequenceLinkedOutputOp.Value = liaraReferencedFinishSequence.UIndex;
            liaraReferencedSequence.WriteProperties(liaraReferencedSequenceProps);

            // copy variables from SequenceReference of KeySequenceObject to SequenceReference for Liara
            var keySequenceReferenceVariableLinks = SeqTools.GetVariableLinksOfNode(keySequenceReference);
            var liaraSequenceReferenceVariableLinks = SeqTools.GetVariableLinksOfNode(liaraSequenceReference);
            if (keySequenceReferenceVariableLinks.Count != 4 || liaraSequenceReferenceVariableLinks.Count != 4)
            {
                throw new SequenceStructureException("SequenceReference does not have 4 variable links");
            }

            var keySequenceReferenceHenchId = keySequenceReferenceVariableLinks[0].LinkedNodes[0];
            if (keySequenceReferenceHenchId is not ExportEntry)
            {
                throw new SequenceStructureException(
                    "First variable link (Henchman ID) of SequenceReference is not ExportLink");
            }

            // copy Henchman ID variable and set to LiaraHenchmanId (14)
            var liaraSequenceReferenceHenchId = (ExportEntry) keySequenceReferenceHenchId.Clone(true);
            package.AddExport(liaraSequenceReferenceHenchId);

            Util.WriteProperty<IntProperty>(
                liaraSequenceReferenceHenchId,
                "IntValue",
                prop => prop.Value = Program.LiaraHenchmanId
            );

            KismetHelper.AddObjectToSequence(liaraSequenceReferenceHenchId, Sequence);
            liaraSequenceReferenceVariableLinks[0].LinkedNodes[0] = liaraSequenceReferenceHenchId;

            var keySequenceReferenceFirstFound = keySequenceReferenceVariableLinks[3].LinkedNodes[0];
            if (keySequenceReferenceFirstFound is not ExportEntry)
            {
                throw new SequenceStructureException(
                    "Third variable of SequenceReference (bFirstFound) is not ExportLink");
            }

            var liaraSequenceReferenceFirstFound = (ExportEntry) keySequenceReferenceFirstFound.Clone(true);
            package.AddExport(liaraSequenceReferenceFirstFound);
            KismetHelper.AddObjectToSequence(liaraSequenceReferenceFirstFound, Sequence);

            liaraSequenceReferenceVariableLinks[3].LinkedNodes[0] = liaraSequenceReferenceFirstFound;

            SeqTools.WriteVariableLinksToNode(liaraSequenceReference, liaraSequenceReferenceVariableLinks);
        }
    }

    class IsTagHenchmanSequence : RelevantSequence
    {
        public IsTagHenchmanSequence(ExportEntry sequence, List<ExportEntry> sequenceObjects,
            ExportEntry keySequenceObject) : base(sequence, sequenceObjects, keySequenceObject)
        {
        }

        public static IsTagHenchmanSequence? CreateIfRelevant(ExportEntry sequence, List<ExportEntry> sequenceObjects)
        {
            var keySequenceObject = sequenceObjects.FindLast(sequenceObject =>
                sequenceObject.ClassName.Equals("SeqCond_CompareName"));
            return keySequenceObject != null
                ? new IsTagHenchmanSequence(sequence, sequenceObjects, keySequenceObject)
                : null;
        }

        public override void HandleSequence(IMEPackage package)
        {
            var outboundLinksOfNode = SeqTools.GetOutboundLinksOfNode(KeySequenceObject);
            var cmpNameLiara = (ExportEntry) ((IEntry) KeySequenceObject).Clone(true);
            KismetHelper.SetComment(cmpNameLiara, "Is *hench_liara*?");
            package.AddExport(cmpNameLiara);

            var falseOutboundLink = Util.GetOutboundLink(outboundLinksOfNode, 1, 0);
            falseOutboundLink.LinkedOp = cmpNameLiara;
            SeqTools.WriteOutboundLinksToNode(KeySequenceObject, outboundLinksOfNode);

            package.FindNameOrAdd("hench_liara");
            Util.WriteProperty<NameProperty>(
                cmpNameLiara,
                "ValueB",
                prop => prop.Value = new NameReference("hench_liara")
            );

            KismetHelper.AddObjectToSequence(cmpNameLiara, Sequence);
        }
    }

    class PlotTransitionSequence : RelevantSequence
    {
        private ExportEntry Compare14 { get; set; }
        private ExportEntry SwitchObject { get; set; }

        public PlotTransitionSequence(
            ExportEntry sequence,
            List<ExportEntry> sequenceObjects,
            ExportEntry keySequenceObject,
            ExportEntry compare14,
            ExportEntry switchObject
        ) : base(sequence, sequenceObjects, keySequenceObject)
        {
            Compare14 = compare14;
            SwitchObject = switchObject;
        }

        public static PlotTransitionSequence? CreateIfRelevant(ExportEntry sequence, List<ExportEntry> sequenceObjects)
        {
            var compareIntObjects = sequenceObjects.FindAll(obj => "SeqCond_CompareInt".Equals(obj.ClassName));
            if (compareIntObjects.Count != 2)
            {
                return null;
            }

            var compare14 = compareIntObjects[1];
            var compare14OutboundLinks = SeqTools.GetOutboundLinksOfNode(compare14);
            var compare14OutboundLink = Util.GetOutboundLink(compare14OutboundLinks, 0, 0, false);
            ExportEntry switchObject;
            if (compare14OutboundLink is not {LinkedOp: ExportEntry op} ||
                !"SeqAct_Switch".Equals((switchObject = op).ClassName))
            {
                return null;
            }

            var switchObjectOutboundLinks = SeqTools.GetOutboundLinksOfNode(switchObject);
            if (switchObjectOutboundLinks == null || switchObjectOutboundLinks.Count != 14)
            {
                return null;
            }

            var lastExecuteTransitionLink = switchObjectOutboundLinks.FindLast(link =>
            {
                if (link.IsEmpty())
                {
                    return false;
                }

                var linkedOp = link[0].LinkedOp;
                return linkedOp is ExportEntry && "BioSeqAct_PMExecuteTransition".Equals(linkedOp.ClassName);
            });

            if (lastExecuteTransitionLink == null)
            {
                return null;
            }

            return new PlotTransitionSequence(
                sequence,
                sequenceObjects,
                (ExportEntry) lastExecuteTransitionLink[0].LinkedOp!,
                compare14,
                switchObject
            );
        }

        public override void HandleSequence(IMEPackage package)
        {
            KismetHelper.SetComment(Compare14, "compare to 15");
            Util.WriteProperty<IntProperty>(Compare14, "ValueB", prop => prop.Value = 15);

            Util.WriteProperty<IntProperty>(SwitchObject, "LinkCount", prop => prop.Value = 15);
            var switchObjectOutboundLinks = SeqTools.GetOutboundLinksOfNode(SwitchObject);

            if (switchObjectOutboundLinks.Count != 14)
            {
                throw new SequenceStructureException("Switch does not have 14 output links");
            }

            var liaraPlotTransition = (ExportEntry) ((IEntry) KeySequenceObject).Clone(true);
            KismetHelper.SetComment(liaraPlotTransition, "Add Liara");

            Util.WriteProperty<IntProperty>(
                liaraPlotTransition,
                "m_nIndex",
                prop => prop.Value = Program.LiaraInSquadPlotTransitionId
            );

            package.AddExport(liaraPlotTransition);
            KismetHelper.AddObjectToSequence(liaraPlotTransition, Sequence);

            KismetHelper.CreateNewOutputLink(SwitchObject, "Link 15", liaraPlotTransition);
        }
    }

    static class Util
    {
        public static void WriteProperty<T>(ExportEntry entry, string propertyName, Action<T> valueSetter)
            where T : Property
        {
            var props = entry.GetProperties();
            var prop = props.GetProp<T>(propertyName);
            if (prop == null)
            {
                throw new SequenceStructureException($"Object is missing {propertyName} prop");
            }

            valueSetter.Invoke(prop);
            entry.WriteProperties(props);
        }

        public static SeqTools.OutboundLink GetOutboundLink(List<List<SeqTools.OutboundLink>> outboundLinks,
            int outboundIdx, int linkIdx, bool require = true)
        {
            if (outboundIdx >= outboundLinks.Count)
            {
                if (require)
                {
                    throw new SequenceStructureException($"Object does not have {outboundIdx + 1} output links");
                }

                return null!;
            }

            var outputLinks = outboundLinks[outboundIdx];

            if (linkIdx >= outputLinks.Count)
            {
                if (require)
                {
                    throw new SequenceStructureException($"Output link does not have {linkIdx + 1} links");
                }

                return null!;
            }

            var outputLink = outputLinks[linkIdx];

            return outputLink;
        }

        public static void CopyAllSequenceObjects(
            IEnumerable<ExportEntry> sequenceObjects,
            ExportEntry sequence,
            IMEPackage package
        )
        {
            var idxRelinkDictionary = new Dictionary<int, int>();
            var newSequenceObjects = new List<ExportEntry>();

            foreach (var sequenceObject in sequenceObjects)
            {
                var newSequenceObject = (ExportEntry) ((IEntry) sequenceObject).Clone(false);
                package.AddExport(newSequenceObject);
                KismetHelper.AddObjectToSequence(newSequenceObject, sequence);
                idxRelinkDictionary[sequenceObject.UIndex] = newSequenceObject.UIndex;
                newSequenceObjects.Add(newSequenceObject);
            }

            // fix links
            foreach (var newSequenceObject in newSequenceObjects)
            {
                var outboundLinksOfNode = SeqTools.GetOutboundLinksOfNode(newSequenceObject);
                var variableLinksOfNode = SeqTools.GetVariableLinksOfNode(newSequenceObject);

                if (outboundLinksOfNode != null && !outboundLinksOfNode.IsEmpty())
                {
                    foreach (var outboundLinks in outboundLinksOfNode)
                    {
                        foreach (var outboundLink in outboundLinks)
                        {
                            var outboundLinkLinkedOp = outboundLink.LinkedOp;
                            if (outboundLinkLinkedOp == null)
                            {
                                continue;
                            }

                            var newIdx = idxRelinkDictionary[outboundLinkLinkedOp.UIndex];
                            outboundLink.LinkedOp = package.GetUExport(newIdx);
                        }
                    }

                    SeqTools.WriteOutboundLinksToNode(newSequenceObject, outboundLinksOfNode);
                }

                if (variableLinksOfNode != null && !variableLinksOfNode.IsEmpty())
                {
                    foreach (var varLinkInfo in variableLinksOfNode)
                    {
                        for (var i = 0; i < varLinkInfo.LinkedNodes.Count; i++)
                        {
                            var oldIdx = varLinkInfo.LinkedNodes[i].UIndex;
                            varLinkInfo.LinkedNodes[i] = package.GetUExport(idxRelinkDictionary[oldIdx]);
                        }
                    }

                    SeqTools.WriteVariableLinksToNode(newSequenceObject, variableLinksOfNode);
                }
            }
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