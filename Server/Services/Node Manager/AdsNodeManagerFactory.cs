// Ignore Spelling: Plc Namespaces

using AdsSimplifiedInterface;
using Opc.Ua;
using Opc.Ua.Server;

namespace Server.Services
{
    /// <summary>
    /// Factory for creating a node manager for the server.
    /// </summary>
    /// <remarks>
    /// Constructor for the an ADS Node Manager Factory
    /// </remarks>
    /// <param name="plc">PLC Interface</param>
    /// <param name="logger">Log System</param>
    public class AdsNodeManagerFactory(AdsInterface plc, ILogger<AdsNodeManager> logger) : INodeManagerFactory
    {
        #region Members
        /// <summary>
        /// PLC Interface
        /// </summary>
        private readonly AdsInterface _plc = plc;
        /// <summary>
        /// Log System
        /// </summary>
        private readonly ILogger<AdsNodeManager> _logger = logger;
        #endregion

        #region INodeManagerFactory
        /// <inheritdoc/>
        public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration)
        {
            return new AdsNodeManager(_plc, _logger, server, configuration, [.. NamespacesUris]);
        }

        /// <inheritdoc/>
        public StringCollection NamespacesUris
        {
            get
            {
                return [
                    "PLC",
                    "PLCInstance"
                ];
            }
        }
        #endregion
    }
}