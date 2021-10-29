#nullable enable
using System.Collections.Generic;
using System.Linq;
using LegendaryExplorerCore.Helpers;
using LegendaryExplorerCore.Kismet;
using LegendaryExplorerCore.Packages;
using LegendaryExplorerCore.Unreal;

namespace auto_patcher
{
    public abstract class RelevantSequence
    {
        public ExportEntry Sequence { get; }
        public List<ExportEntry> SequenceObjects { get; }
        public ExportEntry KeySequenceObject { get; }

        public RelevantSequence(ExportEntry sequence, List<ExportEntry> sequenceObjects, ExportEntry keySequenceObject)
        {
            Sequence = sequence;
            SequenceObjects = sequenceObjects;
            KeySequenceObject = keySequenceObject;
        }

        public abstract void HandleSequence(IMEPackage package);
    }

    public class LookupHenchmanFromPlotManagerSequence : RelevantSequence
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
            Util.RelinkOutputLink(keySequenceReference, 0, 0, checkStateLiara);

            // link CheckState object Liara to SequenceObject for Liara if true
            Util.RelinkOutputLink(checkStateLiara, 0, 0, liaraSequenceReference);

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

    public class IsTagHenchmanSequence : RelevantSequence
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

    public class PlotTransitionSequence : RelevantSequence
    {
        private ExportEntry Compare14 { get; }
        private ExportEntry SwitchObject { get; }

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

    public abstract class GenericCheckStateExecuteTransitionSequence : RelevantSequence
    {
        private int CheckStateId { get; }
        private int TransitionId { get; }

        public GenericCheckStateExecuteTransitionSequence(
            ExportEntry sequence,
            List<ExportEntry> sequenceObjects,
            ExportEntry keySequenceObject,
            int checkStateId,
            int transitionId
        ) : base(sequence, sequenceObjects, keySequenceObject)
        {
            CheckStateId = checkStateId;
            TransitionId = transitionId;
        }

        public override void HandleSequence(IMEPackage package)
        {
            var checkStateLiara = (ExportEntry) ((IEntry) KeySequenceObject).Clone(true);
            package.AddExport(checkStateLiara);
            KismetHelper.AddObjectToSequence(checkStateLiara, Sequence);
            KismetHelper.SetComment(checkStateLiara, "Liara");
            Util.WriteProperty<IntProperty>(
                checkStateLiara,
                "m_nIndex",
                prop => prop.Value = CheckStateId
            );

            Util.RelinkOutputLink(KeySequenceObject, 1, 0, checkStateLiara);

            var keyCheckStateOutboundLinks = SeqTools.GetOutboundLinksOfNode(KeySequenceObject);
            var keyPlotTransition = Util.GetOutboundLink(keyCheckStateOutboundLinks, 0, 0).LinkedOp;
            if (keyPlotTransition is not ExportEntry
                || !"BioSeqAct_PMExecuteTransition".Equals(keyPlotTransition.ClassName))
            {
                throw new SequenceStructureException("True output link of CheckState does not link to plot transition");
            }

            var liaraPlotTransition = (ExportEntry) keyPlotTransition.Clone(true);
            package.AddExport(liaraPlotTransition);
            KismetHelper.AddObjectToSequence(liaraPlotTransition, Sequence);
            KismetHelper.SetComment(liaraPlotTransition, "Liara");
            Util.WriteProperty<IntProperty>(
                liaraPlotTransition,
                "m_nIndex",
                prop => prop.Value = TransitionId
            );

            Util.RelinkOutputLink((ExportEntry) keyPlotTransition, 0, 0, checkStateLiara);
            Util.RelinkOutputLink(checkStateLiara, 0, 0, liaraPlotTransition);
        }
    }

    public class StoreTheHenchmenInTheSquadSequence : GenericCheckStateExecuteTransitionSequence
    {
        public StoreTheHenchmenInTheSquadSequence(
            ExportEntry sequence,
            List<ExportEntry> sequenceObjects,
            ExportEntry keySequenceObject
        ) : base(
            sequence,
            sequenceObjects,
            keySequenceObject,
            Program.LiaraInSquadPlotId,
            Program.LiaraWasInSquadPlotTransitionId
        )
        {
        }

        public static StoreTheHenchmenInTheSquadSequence? CreateIfRelevant(
            ExportEntry sequence,
            List<ExportEntry> sequenceObjects
        )
        {
            var keySequenceObject = sequenceObjects.FindLast(sequenceObject =>
                sequenceObject.ClassName.Equals("BioSeqAct_PMCheckState"));
            return keySequenceObject != null
                ? new StoreTheHenchmenInTheSquadSequence(sequence, sequenceObjects, keySequenceObject)
                : null;
        }

        public static StoreTheHenchmenInTheSquadSequence? GetFromUnnamedSequence(
            ExportEntry packageExport,
            List<ExportEntry> sequenceObjects
        )
        {
            var inputLinksProp = packageExport.GetProperty<ArrayProperty<StructProperty>>("InputLinks");
            if (inputLinksProp == null || inputLinksProp.Count == 0)
            {
                return null;
            }

            var inputLink = inputLinksProp[0];
            var linkDescProp = inputLink.GetProp<StrProperty>("LinkDesc");
            if (linkDescProp == null)
            {
                return null;
            }

            return "Store Henchmen".Equals(linkDescProp.Value)
                ? CreateIfRelevant(packageExport, sequenceObjects)
                : null;
        }
    }

    public class RetrieveTheHenchmenPreviouslyInTheSquadSequence : GenericCheckStateExecuteTransitionSequence
    {
        public RetrieveTheHenchmenPreviouslyInTheSquadSequence(
            ExportEntry sequence,
            List<ExportEntry> sequenceObjects,
            ExportEntry keySequenceObject
        ) : base(
            sequence,
            sequenceObjects,
            keySequenceObject,
            Program.LiaraWasInSquadPlotId,
            Program.LiaraInSquadPlotTransitionId
        )
        {
        }

        public static RetrieveTheHenchmenPreviouslyInTheSquadSequence? CreateIfRelevant(
            ExportEntry sequence,
            List<ExportEntry> sequenceObjects
        )
        {
            var keySequenceObject = sequenceObjects.FindLast(sequenceObject =>
                sequenceObject.ClassName.Equals("BioSeqAct_PMCheckState"));
            return keySequenceObject != null
                ? new RetrieveTheHenchmenPreviouslyInTheSquadSequence(sequence, sequenceObjects, keySequenceObject)
                : null;
        }
    }

    class ReplacedStateTransitionsSequence : RelevantSequence
    {
        private List<ExportEntry> ReplacedStateTransitions { get; }

        public ReplacedStateTransitionsSequence(ExportEntry sequence, List<ExportEntry> sequenceObjects,
            ExportEntry keySequenceObject, List<ExportEntry> replacedStateTransitions) : base(sequence, sequenceObjects,
            keySequenceObject)
        {
            ReplacedStateTransitions = replacedStateTransitions;
        }

        public override void HandleSequence(IMEPackage package)
        {
            foreach (var replacedStateTransition in ReplacedStateTransitions)
            {
                Util.WriteProperty<IntProperty>(
                    replacedStateTransition,
                    "m_nIndex",
                    prop => prop.Value = Program.ReplacedStateEventIds[prop.Value]);
            }
        }
    }
}