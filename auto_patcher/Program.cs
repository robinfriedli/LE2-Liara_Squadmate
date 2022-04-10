#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using CommandLine;
using LegendaryExplorerCore;
using LegendaryExplorerCore.Helpers;

namespace auto_patcher
{
    class Program
    {
        const string ModPackageDir = "..\\DLC_MOD_LiaraSquad\\CookedPCConsole";

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

            [Option("bioh-end-only", Required = false,
                HelpText =
                    "Only generate the BioH_END_Liara_00 file based on the output dir's BioH_Liara_00 file. Ignores -f and -g.")]
            public bool BioHEndOnly { get; set; }

            [Option('b', "batch-count", Required = false,
                HelpText =
                    "The number of batches files are split into for concurrent execution. The actual number of threads is defined by .net's default thread pool configuration. Only applies to handling the entire game directory using -g.")]
            public int BatchCount { get; set; }

            [Option("retain-mount-priority", Required = false,
                HelpText =
                    "Prevent auto_patcher from adjusting the mount priority of this mod automatically to ensure it mounts above mods where files have been patched.")]
            public bool RetainMountPriority { get; set; }
        }

        static void Main(string[] args)
        {
            Parser
                .Default
                .ParseArguments<Options>(args)
                .WithParsed(RunWithOptions);
        }

        static void RunWithOptions(Options options)
        {
            LegendaryExplorerCoreLib.InitLib(null, s => { Console.WriteLine($"ERROR: Failed to save package, {s}"); });
            var autoPatcherLib = new AutoPatcherLib(new ConsoleReporter(options.Verbose));

            var outputDirName = options.OutputDir ?? ModPackageDir;
            if (!Directory.Exists(outputDirName))
            {
                Directory.CreateDirectory(outputDirName);
                if (options.Verbose)
                {
                    Console.WriteLine($"INFO: Created new output dir {outputDirName}");
                }
            }

            if (options.BioHEndOnly)
            {
                Console.WriteLine("INFO: Generating BioH_END_Liara_00");
                autoPatcherLib.GenerateBioHEndLiara(outputDirName);
            }
            else if (options.InputFiles != null && !options.InputFiles.IsEmpty())
            {
                autoPatcherLib.HandleSpecificFiles(options.InputFiles, outputDirName, !options.RetainMountPriority);
            }
            else if (options.GameDir != null && !options.GameDir.IsEmpty())
            {
                autoPatcherLib.HandleGameDir(
                    options.GameDir,
                    outputDirName,
                    options.BatchCount > 0 ? options.BatchCount : AutoPatcherLib.GetDefaultBatchCount(),
                    !options.RetainMountPriority
                );
            }
            else
            {
                Console.WriteLine("Either -g (game dir) or -f (specific files) must be set.");
            }
        }
    }
}