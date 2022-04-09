using System;
using System.Collections.Generic;
using System.Linq;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Kismet;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;

namespace auto_patcher
{
    public static class Util
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

        public static SeqTools.OutboundLink GetOutboundLink(
            List<List<SeqTools.OutboundLink>> outboundLinks,
            int outboundIdx,
            int linkIdx,
            bool require = true
        )
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

        public static void AddLinkToOutputLink(
            ExportEntry sourceNode,
            ExportEntry targetNode,
            int outboundIdx
        )
        {
            var outboundLinksOfNode = SeqTools.GetOutboundLinksOfNode(sourceNode);
            var expectedSize = outboundIdx + 1;
            if (outboundLinksOfNode.Count < expectedSize)
            {
                throw new SequenceStructureException($"Node does not have {expectedSize} outbound links");
            }

            var outboundLinks = outboundLinksOfNode[outboundIdx];
            outboundLinks.Add(SeqTools.OutboundLink.FromTargetExport(targetNode, 0));
            SeqTools.WriteOutboundLinksToNode(sourceNode, outboundLinksOfNode);
        }

        public static void RelinkOutputLink(
            ExportEntry sourceNode,
            int outboundIdx,
            int linkIdx,
            ExportEntry targetNode
        )
        {
            var outboundLinks = SeqTools.GetOutboundLinksOfNode(sourceNode);
            var outboundLink = GetOutboundLink(outboundLinks, outboundIdx, linkIdx);
            outboundLink.LinkedOp = targetNode;
            SeqTools.WriteOutboundLinksToNode(sourceNode, outboundLinks);
        }

        public static ExportEntry CloneSequenceReference(IMEPackage package, ExportEntry sequenceReference)
        {
            var newSequenceReference = (ExportEntry) ((IEntry) sequenceReference).Clone(true);
            package.AddExport(newSequenceReference);

            CloneReferencedSequence(package, newSequenceReference);

            return newSequenceReference;
        }

        public static void CloneReferencedSequence(IMEPackage package, ExportEntry newSequenceReference)
        {
            var referencedSequenceProp = newSequenceReference.GetProperty<ObjectProperty>("oSequenceReference");
            if (referencedSequenceProp == null)
            {
                throw new SequenceStructureException("SequenceReference does not reference a sequence");
            }

            var referencedSequence = package.GetUExport(referencedSequenceProp.Value);
            var newSequence = CloneSequence(package, referencedSequence);

            WriteProperty<ObjectProperty>(
                newSequenceReference,
                "oSequenceReference",
                prop => prop.Value = newSequence.UIndex
            );

            WriteProperty<ObjectProperty>(
                newSequence,
                "ParentSequence",
                prop => prop.Value = newSequenceReference.UIndex
            );

            newSequence.idxLink = newSequenceReference.UIndex;
        }

        public static ExportEntry CloneSequence(
            IMEPackage package,
            ExportEntry source,
            Dictionary<int, int> inheritedIdRelinkDictionary = null
        )
        {
            var newSequence = source.Clone();
            package.AddExport(newSequence);

            var sequenceObjects = SeqTools
                .GetAllSequenceElements(newSequence)
                .Select(entry => (ExportEntry) entry)
                .ToList();

            WriteProperty<ArrayProperty<ObjectProperty>>(
                newSequence,
                "SequenceObjects",
                prop => prop.Clear()
            );

            CopyAllSequenceObjects(sequenceObjects, newSequence, package, true, inheritedIdRelinkDictionary);

            if (inheritedIdRelinkDictionary == null)
            {
                var sequenceActivatedEntries = SeqTools
                    .GetAllSequenceElements(newSequence)
                    .Where(entry => entry is ExportEntry && "SeqEvent_SequenceActivated".Equals(entry.ClassName))
                    .Select(entry => (ExportEntry) entry)
                    .ToList();

                var finishSequenceEntries = SeqTools
                    .GetAllSequenceElements(newSequence)
                    .Where(entry => entry is ExportEntry && "SeqAct_FinishSequence".Equals(entry.ClassName))
                    .Select(entry => (ExportEntry) entry)
                    .ToList();

                var propertyCollection = newSequence.GetProperties();
                var inputLinks = propertyCollection.GetProp<ArrayProperty<StructProperty>>("InputLinks");
                inputLinks.Clear();

                static PropertyCollection GetLinkProps(ExportEntry entry, string desc)
                {
                    var linkProps = new PropertyCollection();
                    linkProps.AddOrReplaceProp(new StrProperty(desc, "LinkDesc"));
                    linkProps.AddOrReplaceProp(new NameProperty(entry.ObjectName, "LinkAction"));
                    linkProps.AddOrReplaceProp(new ObjectProperty(entry, "LinkedOp"));
                    return linkProps;
                }

                foreach (var sequenceActivatedEntry in sequenceActivatedEntries)
                {
                    inputLinks.Add(new StructProperty("SeqOpInputLink", GetLinkProps(sequenceActivatedEntry, "in")));
                }

                var outputLinks = propertyCollection.GetProp<ArrayProperty<StructProperty>>("OutputLinks");
                outputLinks.Clear();
                foreach (var finishSequenceEntry in finishSequenceEntries)
                {
                    var outputLabel = finishSequenceEntry.GetProperty<StrProperty>("OutputLabel") ?? "Out";
                    outputLinks.Add(new StructProperty("SeqOpOutputLink",
                        GetLinkProps(finishSequenceEntry, outputLabel)));
                }

                newSequence.WriteProperties(propertyCollection);
            }

            return newSequence;
        }

        public static void CopyAllSequenceObjects(
            IEnumerable<ExportEntry> sequenceObjects,
            ExportEntry sequence,
            IMEPackage package,
            bool cloneNestedSequences = false,
            Dictionary<int, int> inheritedRelinkDictionary = null
        )
        {
            var idxRelinkDictionary = inheritedRelinkDictionary ?? new Dictionary<int, int>();
            var newSequenceObjects = new List<ExportEntry>();

            foreach (var sequenceObject in sequenceObjects)
            {
                ExportEntry newSequenceObject;
                if (cloneNestedSequences && "Sequence".Equals(sequenceObject.ClassName))
                {
                    newSequenceObject = CloneSequence(package, sequenceObject, idxRelinkDictionary);
                }
                else
                {
                    newSequenceObject = (ExportEntry) ((IEntry) sequenceObject).Clone(false);
                    package.AddExport(newSequenceObject);
                }

                KismetHelper.AddObjectToSequence(newSequenceObject, sequence);
                idxRelinkDictionary[sequenceObject.UIndex] = newSequenceObject.UIndex;
                newSequenceObjects.Add(newSequenceObject);

                if (cloneNestedSequences && "SequenceReference".Equals(newSequenceObject.ClassName))
                {
                    CloneReferencedSequence(package, newSequenceObject);
                }
            }

            // fix links
            foreach (var newSequenceObject in newSequenceObjects)
            {
                var variableLinksOfNode = SeqTools.GetVariableLinksOfNode(newSequenceObject);
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

                void remapLink(StructProperty structProperty)
                {
                    var linkedOp = structProperty.GetProp<ObjectProperty>("LinkedOp");
                    if (linkedOp != null && linkedOp.Value > 0)
                    {
                        linkedOp.Value = idxRelinkDictionary[linkedOp.Value];
                    }
                    else
                    {
                        return;
                    }

                    var linkAction = structProperty.GetProp<NameProperty>("LinkAction");
                    if (linkAction != null && !"None".CaseInsensitiveEquals(linkAction.Value))
                    {
                        linkAction.Value = package.GetUExport(linkedOp.Value).ObjectName;
                    }
                }

                var newSequenceObjectProps = newSequenceObject.GetProperties();

                var inputLinks = newSequenceObjectProps.GetProp<ArrayProperty<StructProperty>>("InputLinks");
                if (inputLinks != null)
                {
                    foreach (var inputLink in inputLinks)
                    {
                        remapLink(inputLink);
                    }
                }

                var outputLinks = newSequenceObjectProps.GetProp<ArrayProperty<StructProperty>>("OutputLinks");
                if (outputLinks != null)
                {
                    foreach (var outputLink in outputLinks)
                    {
                        var links = outputLink.GetProp<ArrayProperty<StructProperty>>("Links");
                        if (links != null)
                        {
                            foreach (var link in links)
                            {
                                remapLink(link);
                            }
                        }

                        remapLink(outputLink);
                    }
                }

                newSequenceObject.WriteProperties(newSequenceObjectProps);
            }
        }

        public static StructProperty CreateSeqVarLinkStruct(
            int linkedUIdx,
            string linkDesc,
            int expectedType,
            string propertyName,
            int minVars,
            int maxVars
        )
        {
            var props = new PropertyCollection();
            props.AddOrReplaceProp(new ArrayProperty<ObjectProperty>(
                new[] {new ObjectProperty(linkedUIdx)},
                new NameReference("LinkedVariables")
            ));
            props.AddOrReplaceProp(new StrProperty(linkDesc, new NameReference("LinkDesc")));
            props.AddOrReplaceProp(new ObjectProperty(expectedType, new NameReference("ExpectedType")));
            props.AddOrReplaceProp(new NameProperty(new NameReference("None"), new NameReference("LinkVar")));
            props.AddOrReplaceProp(new NameProperty(new NameReference(propertyName),
                new NameReference("PropertyName")));
            props.AddOrReplaceProp(new IntProperty(minVars, new NameReference("MinVars")));
            props.AddOrReplaceProp(new IntProperty(maxVars, new NameReference("MaxVars")));
            props.AddOrReplaceProp(new BoolProperty(false, new NameReference("bWriteable")));
            props.AddOrReplaceProp(new BoolProperty(false, new NameReference("bModifiesLinkedObject")));
            props.AddOrReplaceProp(new BoolProperty(false, new NameReference("bAllowAnyType")));
            return new StructProperty("SeqVarLink", props);
        }
    }
}