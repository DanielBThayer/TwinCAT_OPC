// Ignore Spelling: Plc namespaces

using AdsSimplifiedInterface;
using Opc.Ua;
using Opc.Ua.Server;

namespace Server.Services
{
    /// <summary>
    /// Node Manager for the PLC
    /// </summary>
    public class AdsNodeManager : CustomNodeManager2, INodeManager2, INodeIdFactory, IDisposable
    {
        #region Members
        /// <summary>
        /// PLC Interface
        /// </summary>
        private readonly AdsInterface _plc;
        /// <summary>
        /// Log System
        /// </summary>
        private readonly ILogger<AdsNodeManager> _logger;
        /// <summary>
        /// Variables in the address space
        /// </summary>
        private readonly Dictionary<string, BaseDataVariableState> _variables;
        /// <summary>
        /// Last assigned node number
        /// </summary>
        private uint LastNodeNumber;
        #endregion

        #region Constructors
        /// <summary>
        /// Main constructor
        /// </summary>
        /// <param name="plc">PLC interface</param>
        /// <param name="logger">Log System</param>
        /// <param name="server">OPC UA Server Instance</param>
        /// <param name="configuration">Configuration Information</param>
        /// <param name="namespaces">Array of namespaces</param>
        public AdsNodeManager(AdsInterface plc, ILogger<AdsNodeManager> logger, IServerInternal server, ApplicationConfiguration configuration, string[] namespaces)
            : base(server, configuration, namespaces)
        {
            // Copy over information
            _plc = plc;
            _logger = logger;

            // Initialize the variables
            _variables = [];
            LastNodeNumber = 0;

            // Set the NodeId factory to this class
            SystemContext.NodeIdFactory = this;
        }
        #endregion

        #region INodeIdFactory
        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node is BaseInstanceState baseInstanceState && baseInstanceState.Parent != null && baseInstanceState.Parent.NodeId.Identifier is string text)
            {
                return new NodeId(text + "_" + baseInstanceState.SymbolicName, baseInstanceState.Parent.NodeId.NamespaceIndex);
            }

            return node.NodeId;
        }
        #endregion

        #region INodeManager
        /// <inheritdoc/>
        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (base.Lock)
            {
                if (!externalReferences.TryGetValue(Opc.Ua.ObjectIds.ObjectsFolder, out IList<IReference>? value))
                {
                    value = (externalReferences[Opc.Ua.ObjectIds.ObjectsFolder] = []);
                }

                FolderState folderState = CreateFolder(null, "PLC", "PLC");
                folderState.AddReference(35u, isInverse: true, Opc.Ua.ObjectIds.ObjectsFolder);
                value.Add(new NodeStateReference(35u, isInverse: false, folderState.NodeId));
                folderState.EventNotifier = 1;
                AddRootNotifier(folderState);
                List<BaseDataVariableState> list2 = [];
                try
                {
                    // Get the variables from the PLC
                    List<string> variables = _plc.GetVariables();

                    // Create the folder structure
                    Dictionary<string, FolderState> folders = CreateFolderStructure(folderState, variables);

                    // Create the variables
                    foreach (string variable in variables)
                    {
                        string folder = variable;
                        if (!variables.Any(x => !x.Equals(variable, StringComparison.OrdinalIgnoreCase) && x.Contains(variable + ".", StringComparison.OrdinalIgnoreCase)))
                        {
                            folder = variable[..variable.LastIndexOf('.')];
                        }

                        if (!folders.TryGetValue(folder, out FolderState? parent))
                        {
                            parent = folderState;
                        }

                        PlcVariableTypeInfo variableInfo = _plc.GetVariableInfos(variable).First();

                        list2.Add(ProcessVariable(parent, variable, variableInfo));
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Error creating the AdsNodeManager address space.");
                }

                AddPredefinedNode(base.SystemContext, folderState);
            }
        }

        /// <summary>
        /// Creates the folder structure for the variables
        /// </summary>
        /// <param name="root">Root folder for the current variable list</param>
        /// <param name="variables">List of variables</param>
        /// <returns>Dictionary of variables to their folder structure map</returns>
        private Dictionary<string, FolderState> CreateFolderStructure(FolderState root, List<string> variables)
        {
            Dictionary<string, FolderState> folders = [];
            foreach (string variable in variables)
            {
                string[] parts;
                FolderState parent;
                string name;

                // ToDO: Update to handle arrays

                // Check if it is a structure/array variable
                if (!variables.Any(x => !x.Equals(variable, StringComparison.OrdinalIgnoreCase) && x.Contains(variable + ".", StringComparison.OrdinalIgnoreCase)))
                {
                    // Make sure the parent exists
                    parts = variable[..variable.LastIndexOf('.')].Split('.');
                    parent = root;
                    name = string.Empty;
                    for (int index = 0; index < parts.Length; index++)
                    {
                        string folder = parts[index];

                        if (string.IsNullOrEmpty(name))
                        {
                            name = folder;
                        }
                        else
                        {
                            name += "." + folder;
                        }

                        if (!folders.TryGetValue(name, out FolderState? folderState2))
                        {
                            folderState2 = CreateFolder(parent, folder, folder);
                            folders.Add(name, folderState2);
                        }

                        parent = folderState2;
                    }

                    continue;
                }

                // Create the folder tree
                parts = variable.Split('.');
                parent = root;
                name = string.Empty;
                for (int index = 0; index < parts.Length; index++)
                {
                    string folder = parts[index];

                    if (string.IsNullOrEmpty(name))
                    {
                        name = folder;
                    }
                    else
                    {
                        name += "." + folder;
                    }

                    if (!folders.TryGetValue(name, out FolderState? folderState2))
                    {
                        folderState2 = CreateFolder(parent, folder, folder);
                        folders.Add(name, folderState2);
                    }

                    parent = folderState2;
                }
            }

            return folders;
        }

        /// <summary>
        /// Processes a variable for inclusion in the address space
        /// </summary>
        /// <param name="parent">Parent node of the variable</param>
        /// <param name="instancePath">Path to the variable in the PLC</param>
        /// <param name="variableInfo">Information about the variable data type</param>
        /// <returns>OPC UA variable state</returns>
        private BaseDataVariableState ProcessVariable(NodeState parent, string instancePath, PlcVariableTypeInfo variableInfo)
        {
            string name = instancePath.Contains('.') ? instancePath[(instancePath.LastIndexOf('.') + 1)..] : instancePath;
            BaseDataVariableState baseDataVariableState = CreateVariable(parent, instancePath, name, ConvertDataType(variableInfo), variableInfo.IsArray ? 1 : 0);

            if (variableInfo.Children == null || variableInfo.Children.Count == 0)
            {
                _plc.TryGetValue(instancePath, out object? value);
                baseDataVariableState.Value = value;
                baseDataVariableState.Timestamp = DateTime.UtcNow;
            }
            baseDataVariableState.Description = variableInfo.Comment;
            baseDataVariableState.OnReadValue = OnVariableRead;
            if (!variableInfo.IsReadOnly)
            {
                baseDataVariableState.OnWriteValue = OnVariableWrite;
            }

            // Map the variable path to the OPC UA namespace variable
            _variables.Add(instancePath, baseDataVariableState);

            // Add callback for changes
            _plc.AddNotification(instancePath, OnNotification);

            return baseDataVariableState;
        }

        /// <summary>
        /// Converts the PLC data type to an OPC UA data type
        /// </summary>
        /// <param name="variableInfo">PLC Variable Data Type information</param>
        /// <returns>OPC UA data type for the variable</returns>
        private static NodeId ConvertDataType(PlcVariableTypeInfo variableInfo)
        {
            string dataType = string.IsNullOrEmpty(variableInfo.BaseDataType) ? variableInfo.DataType : variableInfo.BaseDataType;

            return dataType.ToLower() switch
            {
                "bool" => (NodeId)DataTypes.Boolean,
                "sbyte" => (NodeId)DataTypes.SByte,
                "byte" => (NodeId)DataTypes.Byte,
                "word" or "int" or "int16" => (NodeId)DataTypes.Int16,
                "uint" or "uint16" => (NodeId)DataTypes.UInt16,
                "dint" or "dword" or "int32" => (NodeId)DataTypes.Int32,
                "udint" or "date" or "time" or "dt" or "uint32" => (NodeId)DataTypes.UInt32,
                "lint" or "int64" => (NodeId)DataTypes.Int64,
                "ulint" or "uint64" => (NodeId)DataTypes.UInt64,
                "real" or "single" => (NodeId)DataTypes.Float,
                "lreal" or "double" => (NodeId)DataTypes.Double,
                "wstring" or "string" => (NodeId)DataTypes.String,
                "enum" => (NodeId)DataTypes.Enumeration,
                _ => (NodeId)DataTypes.Structure,
            };
        }

        /// <summary>
        /// Creates a new folder in the address space
        /// </summary>
        /// <param name="parent">Parent of the folder</param>
        /// <param name="path">Path to the PLC structure</param>
        /// <param name="name">Name of the PLC structure</param>
        /// <returns>OPC UA folder state</returns>
        private FolderState CreateFolder(NodeState? parent, string path, string name)
        {
            LastNodeNumber++;
            FolderState folderState = new(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = 35u,
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.FolderType,
                NodeId = new NodeId(LastNodeNumber, base.NamespaceIndex),
                BrowseName = new QualifiedName(path, base.NamespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                EventNotifier = 0
            };
            parent?.AddChild(folderState);
            return folderState;
        }

        /// <summary>
        /// Creates a new variable in the address space
        /// </summary>
        /// <param name="parent">Parent to the variable being created</param>
        /// <param name="path">Path to the PLC variable</param>
        /// <param name="name">Name of the PLC variable (i.e. variable's name)</param>
        /// <param name="dataType">Data type of the variable (OPC UA data type)</param>
        /// <param name="valueRank">Number of dimensions in the array, if it is an array</param>
        /// <returns>OPC UA variable state</returns>
        private BaseDataVariableState CreateVariable(NodeState parent, string path, string name, NodeId dataType, int valueRank)
        {
            LastNodeNumber++;
            BaseDataVariableState baseDataVariableState = new(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = 35u,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                NodeId = new NodeId(LastNodeNumber, base.NamespaceIndex),
                BrowseName = new QualifiedName(path, base.NamespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.Historizing,
                UserWriteMask = AttributeWriteMask.Historizing,
                DataType = dataType,
                ValueRank = valueRank,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                Historizing = false,
                Value = null,
                StatusCode = 0u,
                Timestamp = DateTime.UtcNow
            };

            switch (valueRank)
            {
                case 1:
                    baseDataVariableState.ArrayDimensions = new ReadOnlyList<uint>([0u]);
                    break;
                case 2:
                    baseDataVariableState.ArrayDimensions = new ReadOnlyList<uint>([0u, 0u]);
                    break;
            }

            parent?.AddChild(baseDataVariableState);
            return baseDataVariableState;
        }

        /// <inheritdoc/>
        public override void DeleteAddressSpace()
        {
            lock (base.Lock)
            {
            }
        }

        /// <inheritdoc/>
        protected override NodeHandle? GetManagerHandle(ServerSystemContext context, NodeId nodeId, IDictionary<NodeId, NodeState> cache)
        {
            lock (base.Lock)
            {
                if (!IsNodeIdInNamespace(nodeId))
                {
                    return null;
                }

                if (!PredefinedNodes.TryGetValue(nodeId, out NodeState? value))
                {
                    return null;
                }

                return new NodeHandle
                {
                    NodeId = nodeId,
                    Node = value,
                    Validated = true
                };
            }
        }

        /// <inheritdoc/>
        protected override NodeState? ValidateNode(ServerSystemContext context, NodeHandle handle, IDictionary<NodeId, NodeState> cache)
        {
            if (handle == null)
            {
                return null;
            }

            if (handle.Validated)
            {
                return handle.Node;
            }

            return null;
        }
        #endregion

        #region Event Handlers
        /// <summary>
        /// Updates the OPC UA variable with the new value pushed from the PLC
        /// </summary>
        /// <param name="instancePath">PLC variable path</param>
        /// <param name="oldValue">Old value</param>
        /// <param name="newValue">New value</param>
        private void OnNotification(string instancePath, byte[] oldValue, byte[] newValue)
        {
            if (_variables.TryGetValue(instancePath, out BaseDataVariableState? variable))
            {
                variable.Value = newValue;
                variable.Timestamp = DateTime.UtcNow;
                variable.ClearChangeMasks(SystemContext, false);
            }
        }

        /// <summary>
        /// Updates the OPC UA variable with the new value read from the PLC
        /// </summary>
        /// <param name="context">Context of the request</param>
        /// <param name="node">State of the node being read</param>
        /// <param name="indexRange">Range information on the node being read</param>
        /// <param name="dataEncoding">Encoding information for the node</param>
        /// <param name="value">Value read from the PLC</param>
        /// <param name="statusCode">Status of the variable</param>
        /// <param name="timestamp">Timestamp information for the read</param>
        /// <returns>Results to the request</returns>
        private ServiceResult OnVariableRead(ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding, ref object? value, ref StatusCode statusCode, ref DateTime timestamp)
        {
            if (node.BrowseName.Name is string instancePath)
            {
                value = _plc.GetValue(instancePath);
                timestamp = DateTime.UtcNow;
            }

            _logger.LogDebug("Read of {variable}: {value}", node.BrowseName.Name, value);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Updates the PLC variable with the new value written from the OPC UA Client
        /// </summary>
        /// <param name="context">Context of the request</param>
        /// <param name="node">State of the node being read</param>
        /// <param name="indexRange">Range information on the node being read</param>
        /// <param name="dataEncoding">Encoding information for the node</param>
        /// <param name="value">Value to write to the PLC</param>
        /// <param name="statusCode">Status of the variable</param>
        /// <param name="timestamp">Timestamp information for the read</param>
        /// <returns>Results to the request</returns>
        private ServiceResult OnVariableWrite(ISystemContext context, NodeState node, NumericRange indexRange, QualifiedName dataEncoding, ref object value, ref StatusCode statusCode, ref DateTime timestamp)
        {
            if (node.BrowseName.Name is string instancePath)
            {
                _plc.SetValue(instancePath, value);
            }

            _logger.LogDebug("Write to {variable}: {value}", node.BrowseName.Name, value);
            return ServiceResult.Good;
        }
        #endregion

        #region IDisposable Support
        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
        #endregion
    }
}