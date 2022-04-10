#nullable enable
using System.Collections.Generic;
using System.Linq;
using LegendaryExplorerCore.Gammtek.Extensions.Collections.Generic;
using LegendaryExplorerCore.Kismet;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;

namespace auto_patcher
{
    public interface IPackageHandler
    {
        public void HandlePackage(IMEPackage package);
    }

    class TrackGestureInterpGroup : IPackageHandler
    {
        private ExportEntry InterpGroup;
        private ObjectProperty TemplateObjProp;

        public TrackGestureInterpGroup(ExportEntry interpGroup, ObjectProperty templateObjProp)
        {
            InterpGroup = interpGroup;
            TemplateObjProp = templateObjProp;
        }

        public static TrackGestureInterpGroup? CreateIfRelevant(IMEPackage package, ExportEntry packageExport)
        {
            var interpTracks = packageExport.GetProperty<ArrayProperty<ObjectProperty>>("InterpTracks");

            if (interpTracks == null || interpTracks.Count < 11)
            {
                return null;
            }

            ObjectProperty? templateTrackProp = null;
            ISet<string> foundActorTags = new HashSet<string>();

            foreach (var objProp in interpTracks)
            {
                var entry = objProp.ResolveToEntry(package);
                if (entry is not ExportEntry track)
                {
                    continue;
                }

                var nameProperty = track.GetProperty<NameProperty>("m_nmFindActor");
                if (nameProperty != null)
                {
                    var name = nameProperty.Value.Name;
                    foundActorTags.Add(name);
                    if ("hench_mystic".Equals(name))
                    {
                        templateTrackProp = objProp;
                    }
                }
            }

            if (templateTrackProp == null || foundActorTags.Contains(AutoPatcherLib.LiaraHenchTag))
            {
                return null;
            }

            var foundHenchTags = AutoPatcherLib.HenchTags.Intersect(foundActorTags);
            if (foundHenchTags.Count() >= 11)
            {
                return new TrackGestureInterpGroup(
                    packageExport,
                    templateTrackProp
                );
            }

            return null;
        }

        public void HandlePackage(IMEPackage package)
        {
            var liaraTrackObjProp = TemplateObjProp.DeepClone();
            var templateTrackProp = TemplateObjProp.ResolveToEntry(package);
            var liaraTrackProp = (ExportEntry) templateTrackProp.Clone(true);
            package.AddExport(liaraTrackProp);
            liaraTrackObjProp.Value = liaraTrackProp.UIndex;

            package.FindNameOrAdd(AutoPatcherLib.LiaraHenchTag);
            Util.WriteProperty<NameProperty>(
                liaraTrackProp,
                "m_nmFindActor",
                nameProp => nameProp.Value = new NameReference(AutoPatcherLib.LiaraHenchTag)
            );

            Util.WriteProperty<StrProperty>(
                liaraTrackProp,
                "TrackTitle",
                strProp => strProp.Value = "Prop -- hench_liara"
            );

            Util.WriteProperty<ArrayProperty<ObjectProperty>>(
                InterpGroup,
                "InterpTracks",
                arr => arr.Add(liaraTrackObjProp)
            );
        }

        public override string ToString()
        {
            return GetType().Name;
        }
    }

    class BioDNorTriggerStreamsPackageHandler : IPackageHandler
    {
        public override string ToString()
        {
            return GetType().Name;
        }

        public void HandlePackage(IMEPackage package)
        {
            package.FindNameOrAdd("BioH_Liara");
            package.FindNameOrAdd("BioD_Nor_109Liara");

            foreach (var packageExport in package.Exports)
            {
                if (!"BioTriggerStream".Equals(packageExport.ClassName))
                {
                    continue;
                }

                var props = packageExport.GetProperties();
                var streamingStatesProperty = props.GetProp<ArrayProperty<StructProperty>>("StreamingStates");
                if (streamingStatesProperty == null)
                {
                    continue;
                }

                bool writeRequired = false;

                foreach (var structProperty in streamingStatesProperty)
                {
                    writeRequired |= AddNameToRelevantChunk(structProperty, "VisibleChunkNames");
                    writeRequired |= AddNameToRelevantChunk(structProperty, "LoadChunkNames");
                }

                if (writeRequired)
                {
                    packageExport.WriteProperties(props);
                }
            }
        }

        static bool AddNameToRelevantChunk(StructProperty structProperty, string chunkName)
        {
            var chunksNames = structProperty.GetProp<ArrayProperty<NameProperty>>(chunkName);
            if (chunksNames == null)
            {
                return false;
            }

            var index = chunksNames.FindIndex(name => "BioD_Nor_105CerberusCrew".Equals(name.Value.Name));
            if (index >= 0)
            {
                chunksNames.InsertRange(index, new NameProperty[]
                {
                    new("BioH_Liara"),
                    new("BioD_Nor_109Liara")
                });
                return true;
            }

            return false;
        }
    }

    class BioPNorStreamingKismetPackageHandler : IPackageHandler
    {
        public override string ToString()
        {
            return GetType().Name;
        }

        public void HandlePackage(IMEPackage package)
        {
            package.FindNameOrAdd("BioD_Nor_109Liara");

            var bioWorldInfo = package.GetUExport(705);

            var props = bioWorldInfo.GetProperties();
            var streamingLevelsProperty = props.GetProp<ArrayProperty<ObjectProperty>>("StreamingLevels");
            if (streamingLevelsProperty == null)
            {
                return;
            }

            var firstStreamingKismet = package.GetUExport(806);

            var newStreamingKismet = (ExportEntry) ((IEntry) firstStreamingKismet).Clone(true);
            package.AddExport(newStreamingKismet);
            Util.WriteProperty<NameProperty>(
                newStreamingKismet,
                "PackageName",
                nameProp => nameProp.Value = new NameReference("BioD_Nor_109Liara")
            );
            var newObjectProperty = new ObjectProperty(newStreamingKismet);

            streamingLevelsProperty.Add(newObjectProperty);
            bioWorldInfo.WriteProperties(props);
        }
    }

    class StrongholdDeactivateLiaraPackageHandler : IPackageHandler
    {
        public override string ToString()
        {
            return GetType().Name;
        }

        public void HandlePackage(IMEPackage package)
        {
            var levelLoadedEvent = package.GetUExport(3436);
            var sequence = package.GetUExport(3671);

            var checkState = SequenceObjectCreator.CreateSequenceObject(package, "BioSeqAct_PMCheckState");
            KismetHelper.AddObjectToSequence(checkState, sequence);
            var checkStateProps = checkState.GetProperties();
            checkStateProps.AddOrReplaceProp(new IntProperty(6931, new NameReference("m_nIndex")));
            checkStateProps.RemoveNamedProperty("VariableLinks");
            checkState.WriteProperties(checkStateProps);

            var activeBool = SequenceObjectCreator.CreateSequenceObject(package, "SeqVar_Bool");
            KismetHelper.AddObjectToSequence(activeBool, sequence);
            var activeBoolProps = activeBool.GetProperties();
            activeBoolProps.AddOrReplaceProp(new IntProperty(0, new NameReference("bValue")));
            activeBool.WriteProperties(activeBoolProps);

            var modifyPropertyPawn =
                SequenceObjectCreator.CreateSequenceObject(package, "BioSeqAct_ModifyPropertyPawn");
            KismetHelper.AddObjectToSequence(modifyPropertyPawn, sequence);
            var modifyPropertyPawnProps = modifyPropertyPawn.GetProperties();

            modifyPropertyPawnProps.AddOrReplaceProp(new ArrayProperty<StructProperty>(
                new[]
                {
                    Util.CreateSeqVarLinkStruct(
                        4798,
                        "Target",
                        -258,
                        "Targets",
                        1,
                        255
                    ),
                    Util.CreateSeqVarLinkStruct(
                        activeBool.UIndex,
                        "Active",
                        -253,
                        "None",
                        0,
                        1
                    )
                },
                new NameReference("VariableLinks")
            ));

            var activePropertyProps = new PropertyCollection();
            activePropertyProps.AddOrReplaceProp(new NameProperty(new NameReference("Active"),
                new NameReference("PropertyName")));
            activePropertyProps.AddOrReplaceProp(new NameProperty(new NameReference("SetActive"),
                new NameReference("ActualPropertyName")));
            activePropertyProps.AddOrReplaceProp(new BoolProperty(true, new NameReference("bDisplayProperty")));
            activePropertyProps.AddOrReplaceProp(new BoolProperty(true, new NameReference("bOldDisplayProperty")));
            activePropertyProps.AddOrReplaceProp(new EnumProperty(
                new NameReference("BPT_OBJECT_FUNCTION"),
                new NameReference("EBioPropertyType"),
                MEGame.LE2,
                new NameReference("ePropertyType")
            ));
            var activeProperty = new StructProperty("BioPropertyInfo", activePropertyProps);
            modifyPropertyPawnProps.AddOrReplaceProp(new ArrayProperty<StructProperty>(
                new[] {activeProperty},
                new NameReference("Properties")
            ));
            modifyPropertyPawn.WriteProperties(modifyPropertyPawnProps);

            Util.AddLinkToOutputLink(checkState, modifyPropertyPawn, 0);
            Util.AddLinkToOutputLink(levelLoadedEvent, checkState, 0);
        }
    }

    class InsertShowMessageActionPackageHandler : IPackageHandler
    {
        private readonly int SequenceExportUIdx;

        private readonly int PrevExportUIdx;

        private readonly int NextExportUIdx;

        private readonly int StrIdx;

        public InsertShowMessageActionPackageHandler(
            int sequenceExportUIdx,
            int prevExportUIdx,
            int nextExportUIdx,
            int strIdx
        )
        {
            SequenceExportUIdx = sequenceExportUIdx;
            PrevExportUIdx = prevExportUIdx;
            NextExportUIdx = nextExportUIdx;
            StrIdx = strIdx;
        }

        public override string ToString()
        {
            return GetType().Name;
        }

        public void HandlePackage(IMEPackage package)
        {
            var prevExport = package.GetUExport(PrevExportUIdx);
            var nextExport = package.GetUExport(NextExportUIdx);
            var sequence = package.GetUExport(SequenceExportUIdx);

            package.FindNameOrAdd("srText");
            package.FindNameOrAdd("srAButton");

            var strRefImport = package.getEntryOrAddImport("Engine.BioSeqVar_StrRef");

            var msgStrRef = createStrRef(package, sequence, StrIdx);
            var okStrRef = createStrRef(package, sequence, 374950);

            var showMessage = SequenceObjectCreator.CreateSequenceObject(package, "BioSeqAct_ShowMessage");
            KismetHelper.AddObjectToSequence(showMessage, sequence);
            var showMessageProps = showMessage.GetProperties();
            showMessageProps.AddOrReplaceProp(new ArrayProperty<StructProperty>(
                new[]
                {
                    Util.CreateSeqVarLinkStruct(
                        msgStrRef.UIndex,
                        "Message",
                        strRefImport.UIndex,
                        "srText",
                        1,
                        1
                    ),
                    Util.CreateSeqVarLinkStruct(
                        okStrRef.UIndex,
                        "AText",
                        strRefImport.UIndex,
                        "srAButton",
                        1,
                        1
                    )
                },
                new NameReference("VariableLinks")
            ));
            showMessage.WriteProperties(showMessageProps);

            Util.RelinkOutputLink(prevExport, 0, 0, showMessage);
            Util.AddLinkToOutputLink(showMessage, nextExport, 1);
        }

        private static ExportEntry createStrRef(IMEPackage package, ExportEntry sequence, int strIdx)
        {
            var msgStrRef = SequenceObjectCreator.CreateSequenceObject(package, "BioSeqVar_StrRef");
            KismetHelper.AddObjectToSequence(msgStrRef, sequence);
            var msgStrRefProps = msgStrRef.GetProperties();
            msgStrRefProps.AddOrReplaceProp(new StringRefProperty(strIdx, new NameReference("m_srValue")));
            msgStrRef.WriteProperties(msgStrRefProps);
            return msgStrRef;
        }
    }

    class BioHLiaraSpawnSequencePackageHandler : IPackageHandler
    {
        public override string ToString()
        {
            return GetType().Name;
        }

        public void HandlePackage(IMEPackage package)
        {
            var levelLoaded = package.GetUExport(12906);
            var remoteEvent = package.GetUExport(12907);
            var log = package.GetUExport(12905);
            var sequence = package.GetUExport(12908);

            var checkState = SequenceObjectCreator.CreateSequenceObject(package, "BioSeqAct_PMCheckState");
            KismetHelper.AddObjectToSequence(checkState, sequence);
            var checkStateProps = checkState.GetProperties();
            checkStateProps.AddOrReplaceProp(new IntProperty(6879, new NameReference("m_nIndex")));
            checkStateProps.RemoveNamedProperty("VariableLinks");
            checkState.WriteProperties(checkStateProps);

            Util.AddLinkToOutputLink(checkState, log, 0);
            Util.RelinkOutputLink(levelLoaded, 0, 0, checkState);
            Util.RelinkOutputLink(remoteEvent, 0, 0, checkState);
        }
    }

    class BioPGlobalPackageHandler : IPackageHandler
    {
        public override string ToString()
        {
            return GetType().Name;
        }

        public void HandlePackage(IMEPackage package)
        {
            package.FindNameOrAdd("stream_liara_00");
            package.FindNameOrAdd("BioH_Liara");
            package.FindNameOrAdd("BioH_Liara_00");

            var spawnHenchmen0CreateHenchmenActivated = HandleCreateHenchmenSequence(package, 214, 169);
            HandleSpawnHenchmanSequence(package, 174, 214, spawnHenchmen0CreateHenchmenActivated);
            var spawnHenchmen1CreateHenchmenActivated = HandleCreateHenchmenSequence(package, 216, 171);
            HandleSpawnHenchmanSequence(package, 175, 216, spawnHenchmen1CreateHenchmenActivated);

            // handle DespawnHenchmen sequence
            var despawnHenchmenSequence = package.GetUExport(212);
            var despawnHenchmenFinishSequence = package.GetUExport(168);
            var despawnHenchmenPrevSetState = SeqTools
                .GetAllSequenceElements(despawnHenchmenSequence)
                .Where(entry =>
                    entry is ExportEntry
                    && Util.GetOutboundLink(
                        SeqTools.GetOutboundLinksOfNode((ExportEntry) entry), 0, 0, false
                    )?.LinkedOp.UIndex == 168
                )
                .Select(entry => (ExportEntry) entry)
                .FirstOrDefault();
            if (despawnHenchmenPrevSetState == null)
            {
                throw new SequenceStructureException("No sequence object has FinishSequence export as output link");
            }

            var despawnHenchmenLiara =
                SequenceObjectCreator.CreateSequenceObject(package, "BioSeqAct_SetStreamingState");
            KismetHelper.AddObjectToSequence(despawnHenchmenLiara, despawnHenchmenSequence);
            var despawnHenchmenLiaraProps = despawnHenchmenLiara.GetProperties();
            despawnHenchmenLiaraProps.RemoveNamedProperty("VariableLinks");
            despawnHenchmenLiaraProps.AddOrReplaceProp(new NameProperty(new NameReference("stream_liara_00"),
                new NameReference("StateName")));
            despawnHenchmenLiara.WriteProperties(despawnHenchmenLiaraProps);
            Util.RelinkOutputLink(despawnHenchmenPrevSetState, 0, 0, despawnHenchmenLiara);
            Util.AddLinkToOutputLink(despawnHenchmenLiara, despawnHenchmenFinishSequence, 0);

            var bioWorldInfo = package.GetUExport(110);
            var bioWorldInfoProps = bioWorldInfo.GetProperties();
            var plotStreamingArr = bioWorldInfoProps.GetProp<ArrayProperty<StructProperty>>("PlotStreaming");
            var liaraPlotStreamingElementProps = new PropertyCollection();
            liaraPlotStreamingElementProps.AddOrReplaceProp(new NameProperty(new NameReference("BioH_Liara_00"),
                new NameReference("ChunkName")));
            liaraPlotStreamingElementProps.AddOrReplaceProp(new BoolProperty(true, new NameReference("bFallback")));
            var liaraPlotStreamingSetProps = new PropertyCollection();
            liaraPlotStreamingSetProps.AddOrReplaceProp(new ArrayProperty<StructProperty>(
                new[] {new StructProperty("PlotStreamingElement", liaraPlotStreamingElementProps)},
                new NameReference("Elements")
            ));
            liaraPlotStreamingSetProps.AddOrReplaceProp(new NameProperty(new NameReference("BioH_Liara"),
                new NameReference("VirtualChunkName")));
            plotStreamingArr.Add(new StructProperty("PlotStreamingSet", liaraPlotStreamingSetProps));
            bioWorldInfo.WriteProperties(bioWorldInfoProps);
        }

        private static ExportEntry HandleCreateHenchmenSequence(
            IMEPackage package,
            int createHenchmenSequenceUIdx,
            int finishSequenceUIdx
        )
        {
            var createHenchmenSequence = package.GetUExport(createHenchmenSequenceUIdx);
            var finishSequence = package.GetUExport(finishSequenceUIdx);

            var sequenceActivated = SequenceObjectCreator.CreateSequenceObject(package, "SeqEvent_SequenceActivated");
            KismetHelper.AddObjectToSequence(sequenceActivated, createHenchmenSequence);
            var sequenceActivatedProps = sequenceActivated.GetProperties();
            sequenceActivatedProps.AddOrReplaceProp(new StrProperty("Liara", new NameReference("Liara")));
            sequenceActivatedProps.RemoveNamedProperty("VariableLinks");
            sequenceActivated.WriteProperties(sequenceActivatedProps);

            var setStreamingState = SequenceObjectCreator.CreateSequenceObject(package, "BioSeqAct_SetStreamingState");
            KismetHelper.AddObjectToSequence(setStreamingState, createHenchmenSequence);
            var setStreamingStateProps = setStreamingState.GetProperties();
            setStreamingStateProps.AddOrReplaceProp(new NameProperty(new NameReference("stream_liara_00"),
                new NameReference("StateName")));
            setStreamingStateProps.AddOrReplaceProp(new BoolProperty(true, new NameReference("NewValue")));
            setStreamingStateProps.RemoveNamedProperty("VariableLinks");
            setStreamingState.WriteProperties(setStreamingStateProps);

            var executeTransition =
                SequenceObjectCreator.CreateSequenceObject(package, "BioSeqAct_PMExecuteTransition");
            KismetHelper.AddObjectToSequence(executeTransition, createHenchmenSequence);
            KismetHelper.SetComment(executeTransition, "Add Liara");
            var executeTransitionProps = executeTransition.GetProperties();
            executeTransitionProps.RemoveNamedProperty("VariableLinks");
            executeTransitionProps.AddOrReplaceProp(new EnumProperty(
                new NameReference("None"),
                new NameReference("EBioAutoSet"),
                MEGame.LE2,
                new NameReference("Transition")
            ));
            executeTransitionProps.AddOrReplaceProp(new IntProperty(10931, new NameReference("m_nIndex")));
            executeTransitionProps.AddOrReplaceProp(new IntProperty(-1, new NameReference("m_nPrevRegionIndex")));
            executeTransitionProps.AddOrReplaceProp(new IntProperty(-1, new NameReference("m_nPrevPlotIndex")));
            executeTransitionProps.AddOrReplaceProp(new EnumProperty(
                new NameReference("None"),
                new NameReference("EBioRegionAutoSet"),
                MEGame.LE2,
                new NameReference("Region")
            ));
            executeTransitionProps.AddOrReplaceProp(new EnumProperty(
                new NameReference("None"),
                new NameReference("EBioPlotAutoSet"),
                MEGame.LE2,
                new NameReference("Plot")
            ));
            executeTransition.WriteProperties(executeTransitionProps);

            Util.AddLinkToOutputLink(sequenceActivated, setStreamingState, 0);
            Util.AddLinkToOutputLink(setStreamingState, executeTransition, 0);
            Util.AddLinkToOutputLink(executeTransition, finishSequence, 0);

            return sequenceActivated;
        }

        private static void HandleSpawnHenchmanSequence(
            IMEPackage package,
            int switchExportUIdx,
            int createHenchmenSequenceUIdx,
            ExportEntry createHenchmenSequenceActivated
        )
        {
            var switchExport = package.GetUExport(switchExportUIdx);
            var createHenchmenSequenceExport = package.GetUExport(createHenchmenSequenceUIdx);
            var switchExportProps = switchExport.GetProperties();
            switchExportProps.GetProp<IntProperty>("LinkCount").Value = 15;
            var switchExportOutputLinks = switchExportProps.GetProp<ArrayProperty<StructProperty>>("OutputLinks");

            var liaraOutputInputLinkProps = new PropertyCollection();
            liaraOutputInputLinkProps.AddOrReplaceProp(new ObjectProperty(createHenchmenSequenceExport,
                new NameReference("LinkedOp")));
            liaraOutputInputLinkProps.AddOrReplaceProp(new IntProperty(13, new NameReference("InputLinkIdx")));
            var liaraOutputLinkProps = new PropertyCollection();
            liaraOutputLinkProps.AddOrReplaceProp(new ArrayProperty<StructProperty>(
                new[] {new StructProperty("SeqOpOutputInputLink", liaraOutputInputLinkProps)},
                new NameReference("Links")
            ));
            liaraOutputLinkProps.AddOrReplaceProp(new StrProperty("Link 15", new NameReference("LinkDesc")));
            liaraOutputLinkProps.AddOrReplaceProp(new NameProperty(new NameReference("None"),
                new NameReference("LinkAction")));
            liaraOutputLinkProps.AddOrReplaceProp(new ObjectProperty(0, new NameReference("LinkedOp")));
            liaraOutputLinkProps.AddOrReplaceProp(new FloatProperty(0, new NameReference("ActivateDelay")));
            liaraOutputLinkProps.AddOrReplaceProp(new BoolProperty(false, new NameReference("bHasImpulse")));
            liaraOutputLinkProps.AddOrReplaceProp(new BoolProperty(false, new NameReference("bDisabled")));
            switchExportOutputLinks.Add(new StructProperty("SeqOpOutputLink", liaraOutputLinkProps));
            switchExport.WriteProperties(switchExportProps);

            var createHenchmenSequenceExportProps = createHenchmenSequenceExport.GetProperties();
            var createHenchmenSequenceInputLinks =
                createHenchmenSequenceExportProps.GetProp<ArrayProperty<StructProperty>>("InputLinks");
            var createHenchmenSequenceInputLinkProps = new PropertyCollection();
            createHenchmenSequenceInputLinkProps.AddOrReplaceProp(new StrProperty("Liara",
                new NameReference("LinkDesc")));
            createHenchmenSequenceInputLinkProps.AddOrReplaceProp(
                new NameProperty(createHenchmenSequenceActivated.ObjectName, new NameReference("LinkAction")));
            createHenchmenSequenceInputLinkProps.AddOrReplaceProp(new ObjectProperty(createHenchmenSequenceActivated,
                new NameReference("LinkedOp")));
            createHenchmenSequenceInputLinkProps.AddOrReplaceProp(new IntProperty(0,
                new NameReference("QueuedActivations")));
            createHenchmenSequenceInputLinkProps.AddOrReplaceProp(new FloatProperty(0,
                new NameReference("ActivateDelay")));
            createHenchmenSequenceInputLinkProps.AddOrReplaceProp(
                new BoolProperty(false, new NameReference("bHasImpulse")));
            createHenchmenSequenceInputLinkProps.AddOrReplaceProp(new BoolProperty(false,
                new NameReference("bDisabled")));
            createHenchmenSequenceInputLinks.Add(new StructProperty("SeqOpInputLink",
                createHenchmenSequenceInputLinkProps));
            createHenchmenSequenceExport.WriteProperties(createHenchmenSequenceExportProps);
        }
    }

    class BioPEndGm1TriggerStreamsPackageHandler : IPackageHandler
    {
        public override string ToString()
        {
            return GetType().Name;
        }

        public void HandlePackage(IMEPackage package)
        {
            package.FindNameOrAdd("stream_liara");
            HandleTriggerStream(package, 1535);
            HandleTriggerStream(package, 1536);
        }

        private static void HandleTriggerStream(IMEPackage package, int triggerStreamUIdx)
        {
            var triggerStream = package.GetUExport(triggerStreamUIdx);
            var triggerStreamProps = triggerStream.GetProperties();
            var streamingStates = triggerStreamProps.GetProp<ArrayProperty<StructProperty>>("StreamingStates");
            var liaraStreamingStateProps = new PropertyCollection();
            liaraStreamingStateProps.AddOrReplaceProp(new ArrayProperty<StructProperty>(new StructProperty[] { },
                new NameReference("VisibleChunkNames")));
            liaraStreamingStateProps.AddOrReplaceProp(new ArrayProperty<StructProperty>(new StructProperty[] { },
                new NameReference("VisibleSoonChunkNames")));
            liaraStreamingStateProps.AddOrReplaceProp(
                new ArrayProperty<StructProperty>(new StructProperty[] { }, new NameReference("LoadChunkNames")));
            liaraStreamingStateProps.AddOrReplaceProp(new NameProperty(new NameReference("stream_liara"),
                new NameReference("StateName")));
            liaraStreamingStateProps.AddOrReplaceProp(new NameProperty(new NameReference("None"),
                new NameReference("InChunkName")));
            liaraStreamingStateProps.AddOrReplaceProp(new FloatProperty(0, new NameReference("DesignFudget")));
            liaraStreamingStateProps.AddOrReplaceProp(new FloatProperty(0, new NameReference("ArtFudget")));
            streamingStates.Add(new StructProperty("BioStreamingState", liaraStreamingStateProps));
            triggerStream.WriteProperties(triggerStreamProps);
        }
    }

    class BioPEndGmStuntHenchPackageHandler : IPackageHandler
    {
        public override string ToString()
        {
            return GetType().Name;
        }

        public void HandlePackage(IMEPackage package)
        {
            package.FindNameOrAdd("load_end_liara");
            package.FindNameOrAdd("visible_end_liara");
            package.FindNameOrAdd("stream_trig_liara");
            package.FindNameOrAdd("BioH_END_Liara");
            package.FindNameOrAdd("BioH_END_Liara_00");

            AddLiaraTriggerStream(package);

            var levelStreamingKismet = (ExportEntry) ((IEntry) package.GetUExport(33)).Clone(true);
            package.AddExport(levelStreamingKismet);
            Util.WriteProperty<NameProperty>(levelStreamingKismet, "PackageName",
                prop => prop.Value = new NameReference("BioH_END_Liara_00"));

            var bioWorldInfo = package.GetUExport(16);
            var bioWorldInfoProps = bioWorldInfo.GetProperties();
            var plotStreamingElements = bioWorldInfoProps.GetProp<ArrayProperty<StructProperty>>("PlotStreaming");
            var liaraPlotStreamingElementProps = new PropertyCollection();
            liaraPlotStreamingElementProps.AddOrReplaceProp(new NameProperty(new NameReference("BioH_END_Liara_00"),
                new NameReference("ChunkName")));
            liaraPlotStreamingElementProps.AddOrReplaceProp(new BoolProperty(true, new NameReference("bFallback")));
            var liaraPlotStreamingSetProps = new PropertyCollection();
            liaraPlotStreamingSetProps.AddOrReplaceProp(new ArrayProperty<StructProperty>(
                new[] {new StructProperty("PlotStreamingElement", liaraPlotStreamingElementProps)},
                new NameReference("Elements")
            ));
            liaraPlotStreamingSetProps.AddOrReplaceProp(new NameProperty(new NameReference("BioH_END_Liara"),
                new NameReference("VirtualChunkName")));
            plotStreamingElements.Add(new StructProperty("PlotStreamingSet", liaraPlotStreamingSetProps));

            var streamingLevels = bioWorldInfoProps.GetProp<ArrayProperty<ObjectProperty>>("StreamingLevels");
            streamingLevels.Add(new ObjectProperty(levelStreamingKismet));

            bioWorldInfo.WriteProperties(bioWorldInfoProps);
        }

        private static void AddLiaraTriggerStream(IMEPackage package)
        {
            var triggerStream = (ExportEntry) ((IEntry) package.GetUExport(2)).Clone(true);
            package.AddExport(triggerStream);
            var triggerStreamProps = triggerStream.GetProperties();
            var streamingStates = triggerStreamProps.GetProp<ArrayProperty<StructProperty>>("StreamingStates");

            foreach (var streamingState in streamingStates)
            {
                var stateName = streamingState.GetProp<NameProperty>("StateName");
                if (stateName == null)
                {
                    continue;
                }

                var visibleChunkNames = streamingState.GetProp<ArrayProperty<NameProperty>>("VisibleChunkNames");
                var loadChunkNames = streamingState.GetProp<ArrayProperty<NameProperty>>("LoadChunkNames");
                var visibleSoonChunkNames =
                    streamingState.GetProp<ArrayProperty<NameProperty>>("VisibleSoonChunkNames");
                visibleChunkNames.Clear();
                loadChunkNames.Clear();
                visibleSoonChunkNames?.Clear();

                if (stateName.Value.Name.StartsWith("load_end_"))
                {
                    stateName.Value = new NameReference("load_end_liara");
                    loadChunkNames.Add(new NameProperty(new NameReference("BioH_END_Liara")));
                }
                else if (stateName.Value.Name.StartsWith("visible_end_"))
                {
                    stateName.Value = new NameReference("visible_end_liara");
                    visibleChunkNames.Add(new NameProperty(new NameReference("BioH_END_Liara")));
                }
            }

            var prevBrushComponent =
                package.GetUExport(triggerStreamProps.GetProp<ObjectProperty>("BrushComponent").Value);
            var brushComponent = (ExportEntry) ((IEntry) prevBrushComponent).Clone(true);
            package.AddExport(brushComponent);
            brushComponent.idxLink = triggerStream.UIndex;

            triggerStreamProps.AddOrReplaceProp(new ObjectProperty(brushComponent,
                new NameReference("BrushComponent")));
            triggerStreamProps.AddOrReplaceProp(new ObjectProperty(brushComponent,
                new NameReference("CollisionComponent")));
            triggerStreamProps.AddOrReplaceProp(new NameProperty(new NameReference("stream_trig_liara"),
                new NameReference("Tag")));
            var location = triggerStreamProps.GetProp<StructProperty>("Location");
            location.GetProp<FloatProperty>("X").Value = 260069.78f;
            location.GetProp<FloatProperty>("Y").Value = -261397.88f;
            location.GetProp<FloatProperty>("Z").Value = -96f;
            triggerStream.WriteProperties(triggerStreamProps);

            package.AddToLevelActorsIfNotThere(triggerStream);
        }
    }

    class BioDEndGm2300ConclusionPackageHandler : IPackageHandler
    {
        public override string ToString()
        {
            return GetType().Name;
        }

        public void HandlePackage(IMEPackage package)
        {
            var missionCompleteSequence = package.GetUExport(7717);
            var failureLog = package.GetUExport(6646);
            var successLog = package.GetUExport(6634);

            var checkLiaraInSquad = SequenceObjectCreator.CreateSequenceObject(package, "BioSeqAct_PMCheckState");
            KismetHelper.AddObjectToSequence(checkLiaraInSquad, missionCompleteSequence);
            var checkLiaraInSquadProps = checkLiaraInSquad.GetProperties();
            checkLiaraInSquadProps.RemoveNamedProperty("VariableLinks");
            checkLiaraInSquadProps.RemoveNamedProperty("InputLinks");
            checkLiaraInSquadProps.AddOrReplaceProp(new IntProperty(6879, "m_nIndex"));
            checkLiaraInSquad.WriteProperties(checkLiaraInSquadProps);

            var failureLogOutputLinks = SeqTools.GetOutboundLinksOfNode(failureLog);
            SeqTools.WriteOutboundLinksToNode(
                failureLog,
                new List<List<SeqTools.OutboundLink>> {new() {new SeqTools.OutboundLink {LinkedOp = checkLiaraInSquad}}}
            );
            SeqTools.SkipSequenceElement(failureLog, null, 0);
            SeqTools.WriteOutboundLinksToNode(failureLog, failureLogOutputLinks);

            var checkVixenInSquad = (ExportEntry) ((IEntry) checkLiaraInSquad).Clone(true);
            package.AddExport(checkVixenInSquad);
            KismetHelper.AddObjectToSequence(checkVixenInSquad, missionCompleteSequence);
            Util.WriteProperty<IntProperty>(
                checkVixenInSquad,
                "m_nIndex",
                prop => prop.Value = 21
            );

            Util.AddLinkToOutputLink(checkLiaraInSquad, checkVixenInSquad, 0);
            Util.AddLinkToOutputLink(checkLiaraInSquad, failureLog, 1);
            Util.AddLinkToOutputLink(checkVixenInSquad, successLog, 0);
            Util.AddLinkToOutputLink(checkVixenInSquad, failureLog, 1);

            var killSquadMemberSequence = package.GetUExport(7769);
            var mysticSquadCheck = package.GetUExport(1937);
            var vixenSquadCheck = package.GetUExport(1933);
            var vampirePartyCheck = package.GetUExport(1939);
            var vampireSequenceReference = package.GetUExport(7959);

            var checkLiaraInSquad2 = (ExportEntry) ((IEntry) checkLiaraInSquad).Clone(true);
            package.AddExport(checkLiaraInSquad2);
            KismetHelper.AddObjectToSequence(checkLiaraInSquad2, killSquadMemberSequence);

            Util.RelinkOutputLink(mysticSquadCheck, 1, 0, checkLiaraInSquad2);
            Util.RelinkOutputLink(vampirePartyCheck, 1, 0, checkLiaraInSquad2);

            var liaraSequenceReference = Util.CloneSequenceReference(package, vampireSequenceReference);
            KismetHelper.AddObjectToSequence(liaraSequenceReference, killSquadMemberSequence);

            Util.RelinkOutputLink(checkLiaraInSquad2, 0, 0, liaraSequenceReference);
            Util.RelinkOutputLink(checkLiaraInSquad2, 1, 0, vixenSquadCheck);

            package.FindNameOrAdd("hench_liara");
            var henchLiaraName = SequenceObjectCreator.CreateSequenceObject(package, "SeqVar_Name");
            KismetHelper.AddObjectToSequence(henchLiaraName, killSquadMemberSequence);
            var henchLiaraNameProps = henchLiaraName.GetProperties();
            henchLiaraNameProps.AddOrReplaceProp(new NameProperty("hench_liara", "NameValue"));
            henchLiaraName.WriteProperties(henchLiaraNameProps);

            var liaraSequenceReferenceProps = liaraSequenceReference.GetProperties();
            var liaraSequenceReferenceVarLinks = SeqTools.GetVariableLinks(liaraSequenceReferenceProps, package);
            var firstTagLink =
                liaraSequenceReferenceVarLinks.FirstOrDefault(varLink => "First_Tag".Equals(varLink.LinkDesc));
            if (firstTagLink == null)
            {
                throw new SequenceStructureException("SequenceReference does not have First_Tag var link");
            }

            firstTagLink.LinkedNodes = new List<IEntry> {henchLiaraName};
            SeqTools.WriteVariableLinksToProperties(liaraSequenceReferenceVarLinks, liaraSequenceReferenceProps);
            liaraSequenceReference.WriteProperties(liaraSequenceReferenceProps);

            KismetHelper.SetComment(
                checkLiaraInSquad,
                "Liara and Miranda cannot be killed here, trigger success if both in squad"
            );
        }
    }
}