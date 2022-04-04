#nullable enable
using System.Collections.Generic;
using System.Linq;
using LegendaryExplorerCore.Gammtek.Extensions.Collections.Generic;
using LegendaryExplorerCore.Kismet;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;

namespace auto_patcher
{
    interface IPackageHandler
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

            if (templateTrackProp == null || foundActorTags.Contains(Program.LiaraHenchTag))
            {
                return null;
            }

            var foundHenchTags = Program.HenchTags.Intersect(foundActorTags);
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

            package.FindNameOrAdd(Program.LiaraHenchTag);
            Util.WriteProperty<NameProperty>(
                liaraTrackProp,
                "m_nmFindActor",
                nameProp => nameProp.Value = new NameReference(Program.LiaraHenchTag)
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

            if (!package.TryGetUExport(705, out var bioWorldInfo))
            {
                bioWorldInfo = package.Exports.FirstOrDefault(export => "BioWorldInfo".Equals(export.ClassName));
            }

            if (bioWorldInfo == null)
            {
                return;
            }

            var props = bioWorldInfo.GetProperties();
            var streamingLevelsProperty = props.GetProp<ArrayProperty<ObjectProperty>>("StreamingLevels");
            if (streamingLevelsProperty == null)
            {
                return;
            }

            if (!package.TryGetUExport(806, out var firstStreamingKismet)
                || !"LevelStreamingKismet".Equals(firstStreamingKismet.ClassName))
            {
                firstStreamingKismet = streamingLevelsProperty
                    .Select(obj => !package.TryGetUExport(obj.Value, out var kismetObject) ? null : kismetObject)
                    .Where(entry =>
                    {
                        if (entry == null)
                        {
                            return false;
                        }

                        var linkedExport = package.GetUExport(entry.idxLink);
                        return linkedExport != null && "PersistentLevel".Equals(linkedExport.ObjectName.Name);
                    })
                    .FirstOrDefault();
            }

            if (firstStreamingKismet == null)
            {
                return;
            }

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
            if (!package.TryGetUExport(3436, out var levelLoadedEvent))
            {
                return;
            }

            if (!package.TryGetUExport(3671, out var sequence))
            {
                return;
            }

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
            if (!package.TryGetUExport(PrevExportUIdx, out var prevExport))
            {
                return;
            }

            if (!package.TryGetUExport(NextExportUIdx, out var nextExport))
            {
                return;
            }

            if (!package.TryGetUExport(SequenceExportUIdx, out var sequence))
            {
                return;
            }

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
}