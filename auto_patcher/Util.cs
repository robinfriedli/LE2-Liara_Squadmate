using System;
using System.Collections.Generic;
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