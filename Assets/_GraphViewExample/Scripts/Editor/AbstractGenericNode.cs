﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Graphing;
using UnityEditor.ShaderGraph;
using UnityEngine;

namespace GeoTetra.GenericGraph
{
    [Serializable]
    public abstract class AbstractGenericNode : INode, ISerializationCallbackReceiver
    {
        protected static List<GenericSlot> TempSlots = new List<GenericSlot>();
        protected static List<IEdge> TempEdges = new List<IEdge>();
        protected static List<PreviewProperty> TempPreviewProperties = new List<PreviewProperty>();

        [NonSerialized]
        private Guid _guid;

        [SerializeField]
        private string _guidSerialized;

        [SerializeField]
        private string _name;

        [SerializeField]
        private DrawState _drawState;

        [NonSerialized]
        private List<ISlot> _slots = new List<ISlot>();

        [SerializeField]
        List<SerializationHelper.JSONSerializedElement> _serializableSlots =
            new List<SerializationHelper.JSONSerializedElement>();

        [NonSerialized]
        private bool _hasError;

        public Identifier tempId { get; set; }

        public IGraph owner { get; set; }

        private OnNodeModified _onModified;

        public void RegisterCallback(OnNodeModified callback)
        {
            _onModified += callback;
        }

        public void UnregisterCallback(OnNodeModified callback)
        {
            _onModified -= callback;
        }

        public void Dirty(ModificationScope scope)
        {
            if (_onModified != null)
                _onModified(this, scope);
        }

        public Guid guid
        {
            get { return _guid; }
        }

        public string name
        {
            get { return _name; }
            set { _name = value; }
        }

        public virtual string DocumentationUrl
        {
            get { return null; }
        }

        public virtual bool canDeleteNode
        {
            get { return true; }
        }

        public DrawState DrawState
        {
            get { return _drawState; }
            set
            {
                _drawState = value;
                Dirty(ModificationScope.Node);
            }
        }

        [SerializeField]
        private bool _previewExpanded = true;

        public bool PreviewExpanded
        {
            get { return _previewExpanded; }
            set
            {
                if (PreviewExpanded == value)
                    return;
                _previewExpanded = value;
                Dirty(ModificationScope.Node);
            }
        }

        // Nodes that want to have a preview area can override this and return true
        public virtual bool HasPreview
        {
            get { return false; }
        }

        public virtual PreviewMode PreviewMode
        {
            get { return PreviewMode.Preview2D; }
        }

        public virtual bool AllowedInSubGraph
        {
            get { return true; }
        }

        public virtual bool AllowedInMainGraph
        {
            get { return true; }
        }

        public virtual bool AllowedInLayerGraph
        {
            get { return true; }
        }

        public virtual bool hasError
        {
            get { return _hasError; }
            protected set { _hasError = value; }
        }

        private string _defaultVariableName;
        private string _nameForDefaultVariableName;
        private Guid _guidForDefaultVariableName;

        string DefaultVariableName
        {
            get
            {
                if (_nameForDefaultVariableName != name || _guidForDefaultVariableName != guid)
                {
                    _defaultVariableName = string.Format("{0}_{1}", NodeUtils.GetHLSLSafeName(name ?? "node"),
                        GuidEncoder.Encode(guid));
                    _nameForDefaultVariableName = name;
                    _guidForDefaultVariableName = guid;
                }

                return _defaultVariableName;
            }
        }

        protected AbstractGenericNode()
        {
            _drawState.expanded = true;
            _guid = Guid.NewGuid();
            Version = 0;
        }

        public Guid RewriteGuid()
        {
            _guid = Guid.NewGuid();
            return _guid;
        }

        public void GetInputSlots<T>(List<T> foundSlots) where T : ISlot
        {
            foreach (var slot in _slots)
            {
                if (slot.isInputSlot && slot is T)
                    foundSlots.Add((T) slot);
            }
        }

        public void GetOutputSlots<T>(List<T> foundSlots) where T : ISlot
        {
            foreach (var slot in _slots)
            {
                if (slot.isOutputSlot && slot is T)
                    foundSlots.Add((T) slot);
            }
        }

        public void GetSlots<T>(List<T> foundSlots) where T : ISlot
        {
            foreach (var slot in _slots)
            {
                if (slot is T)
                    foundSlots.Add((T) slot);
            }
        }

        public virtual void CollectGenericProperties(PropertyCollector properties, GenerationMode generationMode)
        {
            foreach (var inputSlot in this.GetInputSlots<MaterialSlot>())
            {
                var edges = owner.GetEdges(inputSlot.slotReference);
                if (edges.Any())
                    continue;

                inputSlot.AddDefaultProperty(properties, generationMode);
            }
        }

        public string GetSlotValue(int inputSlotId, GenerationMode generationMode)
        {
            var inputSlot = FindSlot<MaterialSlot>(inputSlotId);
            if (inputSlot == null)
                return string.Empty;

            var edges = owner.GetEdges(inputSlot.slotReference).ToArray();

            if (edges.Any())
            {
                var fromSocketRef = edges[0].outputSlot;
                var fromNode = owner.GetNodeFromGuid<AbstractMaterialNode>(fromSocketRef.nodeGuid);
                if (fromNode == null)
                    return string.Empty;

                var slot = fromNode.FindOutputSlot<MaterialSlot>(fromSocketRef.slotId);
                if (slot == null)
                    return string.Empty;

                return ShaderGenerator.AdaptNodeOutput(fromNode, slot.id, inputSlot.concreteValueType);
            }

            return inputSlot.GetDefaultValue(generationMode);
        }

        public static bool ImplicitConversionExists(ConcreteSlotValueType from, ConcreteSlotValueType to)
        {
            if (from == to)
                return true;

            var fromCount = SlotValueHelper.GetChannelCount(from);
            var toCount = SlotValueHelper.GetChannelCount(to);

            if (toCount > 0 && fromCount > 0)
                return true;

            return false;
        }

        public virtual ConcreteSlotValueType ConvertDynamicInputTypeToConcrete(
            IEnumerable<ConcreteSlotValueType> inputTypes)
        {
            var concreteSlotValueTypes = inputTypes as IList<ConcreteSlotValueType> ?? inputTypes.ToList();

            var inputTypesDistinct = concreteSlotValueTypes.Distinct().ToList();
            switch (inputTypesDistinct.Count)
            {
                case 0:
                    return ConcreteSlotValueType.Vector1;
                case 1:
                    return inputTypesDistinct.FirstOrDefault();
                default:
                    // find the 'minumum' channel width excluding 1 as it can promote
                    inputTypesDistinct.RemoveAll(x => x == ConcreteSlotValueType.Vector1);
                    var ordered = inputTypesDistinct.OrderByDescending(x => x);
                    if (ordered.Any())
                        return ordered.FirstOrDefault();
                    break;
            }

            return ConcreteSlotValueType.Vector1;
        }

        public virtual ConcreteSlotValueType ConvertDynamicMatrixInputTypeToConcrete(
            IEnumerable<ConcreteSlotValueType> inputTypes)
        {
            var concreteSlotValueTypes = inputTypes as IList<ConcreteSlotValueType> ?? inputTypes.ToList();

            var inputTypesDistinct = concreteSlotValueTypes.Distinct().ToList();
            switch (inputTypesDistinct.Count)
            {
                case 0:
                    return ConcreteSlotValueType.Matrix2;
                case 1:
                    return inputTypesDistinct.FirstOrDefault();
                default:
                    var ordered = inputTypesDistinct.OrderByDescending(x => x);
                    if (ordered.Any())
                        return ordered.FirstOrDefault();
                    break;
            }

            return ConcreteSlotValueType.Matrix2;
        }

        public virtual void ValidateNode()
        {
            var isInError = false;

            // all children nodes needs to be updated first
            // so do that here
            var slots = ListPool<MaterialSlot>.Get();
            GetInputSlots(slots);
            foreach (var inputSlot in slots)
            {
                inputSlot.hasError = false;

                var edges = owner.GetEdges(inputSlot.slotReference);
                foreach (var edge in edges)
                {
                    var fromSocketRef = edge.outputSlot;
                    var outputNode = owner.GetNodeFromGuid(fromSocketRef.nodeGuid);
                    if (outputNode == null)
                        continue;

                    outputNode.ValidateNode();
                    if (outputNode.hasError)
                        isInError = true;
                }
            }

            ListPool<MaterialSlot>.Release(slots);

            var dynamicInputSlotsToCompare = DictionaryPool<DynamicVectorMaterialSlot, ConcreteSlotValueType>.Get();
            var skippedDynamicSlots = ListPool<DynamicVectorMaterialSlot>.Get();

            var dynamicMatrixInputSlotsToCompare =
                DictionaryPool<DynamicMatrixMaterialSlot, ConcreteSlotValueType>.Get();
            var skippedDynamicMatrixSlots = ListPool<DynamicMatrixMaterialSlot>.Get();

            // iterate the input slots
            TempSlots.Clear();
            GetInputSlots(TempSlots);
            foreach (var inputSlot in TempSlots)
            {
                // if there is a connection
                var edges = owner.GetEdges(inputSlot.slotReference).ToList();
                if (!edges.Any())
                {
                    if (inputSlot is DynamicVectorMaterialSlot)
//                        skippedDynamicSlots.Add(inputSlot as DynamicVectorMaterialSlot);
                    if (inputSlot is DynamicMatrixMaterialSlot)
//                        skippedDynamicMatrixSlots.Add(inputSlot as DynamicMatrixMaterialSlot);
                    continue;
                }

                // get the output details
                var outputSlotRef = edges[0].outputSlot;
                var outputNode = owner.GetNodeFromGuid(outputSlotRef.nodeGuid);
                if (outputNode == null)
                    continue;

                var outputSlot = outputNode.FindOutputSlot<MaterialSlot>(outputSlotRef.slotId);
                if (outputSlot == null)
                    continue;

                if (outputSlot.hasError)
                {
                    inputSlot.hasError = true;
                    continue;
                }

                var outputConcreteType = outputSlot.concreteValueType;
                // dynamic input... depends on output from other node.
                // we need to compare ALL dynamic inputs to make sure they
                // are compatable.
                if (inputSlot is DynamicVectorMaterialSlot)
                {
//                    dynamicInputSlotsToCompare.Add((DynamicVectorMaterialSlot) inputSlot, outputConcreteType);
                    continue;
                }
                else if (inputSlot is DynamicMatrixMaterialSlot)
                {
//                    dynamicMatrixInputSlotsToCompare.Add((DynamicMatrixMaterialSlot) inputSlot, outputConcreteType);
                    continue;
                }

                // if we have a standard connection... just check the types work!
                if (!ImplicitConversionExists(outputConcreteType, inputSlot.concreteValueType))
                    inputSlot.hasError = true;
            }

            // we can now figure out the dynamic slotType
            // from here set all the
            var dynamicType = ConvertDynamicInputTypeToConcrete(dynamicInputSlotsToCompare.Values);
            foreach (var dynamicKvP in dynamicInputSlotsToCompare)
                dynamicKvP.Key.SetConcreteType(dynamicType);
            foreach (var skippedSlot in skippedDynamicSlots)
                skippedSlot.SetConcreteType(dynamicType);

            // and now dynamic matrices
            var dynamicMatrixType = ConvertDynamicMatrixInputTypeToConcrete(dynamicMatrixInputSlotsToCompare.Values);
            foreach (var dynamicKvP in dynamicMatrixInputSlotsToCompare)
                dynamicKvP.Key.SetConcreteType(dynamicMatrixType);
            foreach (var skippedSlot in skippedDynamicMatrixSlots)
                skippedSlot.SetConcreteType(dynamicMatrixType);

            TempSlots.Clear();
            GetInputSlots(TempSlots);
            var inputError = TempSlots.Any(x => x.hasError);

            // configure the output slots now
            // their slotType will either be the default output slotType
            // or the above dynanic slotType for dynamic nodes
            // or error if there is an input error
            TempSlots.Clear();
            GetOutputSlots(TempSlots);
            foreach (var outputSlot in TempSlots)
            {
                outputSlot.hasError = false;

                if (inputError)
                {
                    outputSlot.hasError = true;
                    continue;
                }

                if (outputSlot is DynamicVectorMaterialSlot)
                {
//                    (outputSlot as DynamicVectorMaterialSlot).SetConcreteType(dynamicType);
                    continue;
                }
                else if (outputSlot is DynamicMatrixMaterialSlot)
                {
//                    (outputSlot as DynamicMatrixMaterialSlot).SetConcreteType(dynamicMatrixType);
                    continue;
                }
            }

            isInError |= inputError;
            TempSlots.Clear();
            GetOutputSlots(TempSlots);
            isInError |= TempSlots.Any(x => x.hasError);
            isInError |= CalculateNodeHasError();
            hasError = isInError;

            if (!hasError)
            {
                ++Version;
            }

            ListPool<DynamicVectorMaterialSlot>.Release(skippedDynamicSlots);
            DictionaryPool<DynamicVectorMaterialSlot, ConcreteSlotValueType>.Release(dynamicInputSlotsToCompare);

            ListPool<DynamicMatrixMaterialSlot>.Release(skippedDynamicMatrixSlots);
            DictionaryPool<DynamicMatrixMaterialSlot, ConcreteSlotValueType>.Release(dynamicMatrixInputSlotsToCompare);
        }

        public int Version { get; set; }

        //True if error
        protected virtual bool CalculateNodeHasError()
        {
            return false;
        }

        public virtual void CollectPreviewMaterialProperties(List<PreviewProperty> properties)
        {
            TempSlots.Clear();
            GetInputSlots(TempSlots);
            foreach (var s in TempSlots)
            {
                TempPreviewProperties.Clear();
                TempEdges.Clear();
                owner.GetEdges(s.slotReference, TempEdges);
                if (TempEdges.Any())
                    continue;

                s.GetPreviewProperties(TempPreviewProperties, GetVariableNameForSlot(s.id));
                for (int i = 0; i < TempPreviewProperties.Count; i++)
                {
                    if (TempPreviewProperties[i].name == null)
                        continue;

                    properties.Add(TempPreviewProperties[i]);
                }
            }
        }

        public virtual string GetVariableNameForSlot(int slotId)
        {
            var slot = FindSlot<MaterialSlot>(slotId);
            if (slot == null)
                throw new ArgumentException(
                    string.Format(
                        "Attempting to use MaterialSlot({0}) on node of type {1} where this slot can not be found",
                        slotId, this), "slotId");
            return string.Format("_{0}_{1}", GetVariableNameForNode(),
                NodeUtils.GetHLSLSafeName(slot.shaderOutputName));
        }

        public virtual string GetVariableNameForNode()
        {
            return DefaultVariableName;
        }

        public void AddSlot(ISlot slot)
        {
            if (!(slot is MaterialSlot))
                throw new ArgumentException(string.Format(
                    "Trying to add slot {0} to Material node {1}, but it is not a {2}", slot, this,
                    typeof(MaterialSlot)));

            var addingSlot = (MaterialSlot) slot;
            var foundSlot = FindSlot<MaterialSlot>(slot.id);

            // this will remove the old slot and add a new one
            // if an old one was found. This allows updating values
            _slots.RemoveAll(x => x.id == slot.id);
            _slots.Add(slot);
            slot.owner = this;

            Dirty(ModificationScope.Topological);

            if (foundSlot == null)
                return;

            addingSlot.CopyValuesFrom(foundSlot);
        }

        public void RemoveSlot(int slotId)
        {
            // Remove edges that use this slot
            // no owner can happen after creation
            // but before added to graph
            if (owner != null)
            {
                var edges = owner.GetEdges(GetSlotReference(slotId));

                foreach (var edge in edges.ToArray())
                    owner.RemoveEdge(edge);
            }

            //remove slots
            _slots.RemoveAll(x => x.id == slotId);

            Dirty(ModificationScope.Topological);
        }

        public void RemoveSlotsNameNotMatching(IEnumerable<int> slotIds, bool supressWarnings = false)
        {
            var invalidSlots = _slots.Select(x => x.id).Except(slotIds);

            foreach (var invalidSlot in invalidSlots.ToArray())
            {
                if (!supressWarnings)
                    Debug.LogWarningFormat("Removing Invalid MaterialSlot: {0}", invalidSlot);
                RemoveSlot(invalidSlot);
            }
        }

        public SlotReference GetSlotReference(int slotId)
        {
            var slot = FindSlot<ISlot>(slotId);
            if (slot == null)
                throw new ArgumentException("Slot could not be found", "slotId");
            return new SlotReference(guid, slotId);
        }

        public T FindSlot<T>(int slotId) where T : ISlot
        {
            foreach (var slot in _slots)
            {
                if (slot.id == slotId && slot is T)
                    return (T) slot;
            }

            return default(T);
        }

        public T FindInputSlot<T>(int slotId) where T : ISlot
        {
            foreach (var slot in _slots)
            {
                if (slot.isInputSlot && slot.id == slotId && slot is T)
                    return (T) slot;
            }

            return default(T);
        }

        public T FindOutputSlot<T>(int slotId) where T : ISlot
        {
            foreach (var slot in _slots)
            {
                if (slot.isOutputSlot && slot.id == slotId && slot is T)
                    return (T) slot;
            }

            return default(T);
        }

        public virtual IEnumerable<ISlot> GetInputsWithNoConnection()
        {
            return this.GetInputSlots<ISlot>().Where(x => !owner.GetEdges(GetSlotReference(x.id)).Any());
        }

        public virtual void OnBeforeSerialize()
        {
            _guidSerialized = _guid.ToString();
            _serializableSlots = SerializationHelper.Serialize<ISlot>(_slots);
        }

        public virtual void OnAfterDeserialize()
        {
            if (!string.IsNullOrEmpty(_guidSerialized))
                _guid = new Guid(_guidSerialized);
            else
                _guid = Guid.NewGuid();

            _slots = SerializationHelper.Deserialize<ISlot>(_serializableSlots, GraphUtil.GetLegacyTypeRemapping());
            _serializableSlots = null;
            foreach (var s in _slots)
                s.owner = this;
            UpdateNodeAfterDeserialization();
        }

        public virtual void UpdateNodeAfterDeserialization()
        {
        }

        public bool IsSlotConnected(int slotId)
        {
            var slot = FindSlot<MaterialSlot>(slotId);
            return slot != null && owner.GetEdges(slot.slotReference).Any();
        }
    }
}
