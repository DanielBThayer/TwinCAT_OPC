// Ignore Spelling: Opc plc

using AdsSimplifiedInterface;
using Opc.Ua.Configuration;
using Opc.Ua;
using Opc.Ua.Server;
using System.Text;

namespace Server.Services
{
    /// <summary>
    /// Interface the TwinCAT ADS Server with an OPC UA Clients
    /// </summary>
    public class TwinCatOpcServer
    {
        #region Members
        /// <summary>
        /// Log System
        /// </summary>
        private readonly ILogger<TwinCatOpcServer> _logger;
        /// <summary>
        /// PLC Interface
        /// </summary>
        private readonly AdsInterface _plc;
        private readonly ApplicationInstance _application;
        private readonly ApplicationConfiguration _configuration;
        /// <summary>
        /// Factory for creating a node manager for the server.
        /// </summary>
        private readonly INodeManagerFactory plcNodeManagerFactory;

        private DateTime m_lastEventTime;
        private StandardServer Server;
        #endregion

        #region Constructors
        /// <summary>
        /// Constructor for the TwinCAT OPC Server
        /// </summary>
        /// <param name="config">Configuration Information</param>
        /// <param name="logger">Log System</param>
        /// <param name="plc">PLC Interface</param>
        public TwinCatOpcServer(IConfiguration config, ILogger<TwinCatOpcServer> logger, AdsInterface plc, INodeManagerFactory nodeManagerFactory)
        {
            // Copy over information
            _logger = logger;
            _plc = plc;
            plcNodeManagerFactory = nodeManagerFactory;

            _application = new ApplicationInstance
            {
                ApplicationName = "QuickStarts ReferenceServer",
                ApplicationType = ApplicationType.Server,
                ConfigSectionName = "QuickStarts.ReferenceServer"
            };

            // load the application configuration.
            _configuration = _application.LoadApplicationConfiguration(false).Result;

            // check the application certificate.
            bool certOk = _application.CheckApplicationInstanceCertificate(false, 0).Result;
            if (!certOk)
            {
                throw new Exception("Application instance certificate invalid!");
            }

            // Create server, add additional node managers
            Server = new StandardServer();
            Server.AddNodeManager(plcNodeManagerFactory);

            // start the server.
            _application.Start(Server).Wait();

            // print endpoint info
            var endpoints = _application.Server.GetEndpoints().Select(e => e.EndpointUrl).Distinct();
            foreach (var endpoint in endpoints)
            {
                _logger.LogInformation("Endpoint: {0}", endpoint);
            }

            // print notification on session events
            Server.CurrentInstance.SessionManager.SessionActivated += EventStatus;
            Server.CurrentInstance.SessionManager.SessionClosing += EventStatus;
            Server.CurrentInstance.SessionManager.SessionCreated += EventStatus;

        }
        #endregion

        /// <summary>
        /// Update the session status.
        /// </summary>
        private void EventStatus(Session session, SessionEventReason reason)
        {
            m_lastEventTime = DateTime.UtcNow;
            PrintSessionStatus(session, reason.ToString());
        }

        /// <summary>
        /// Output the status of a connected session.
        /// </summary>
        private void PrintSessionStatus(Session session, string reason, bool lastContact = false)
        {
            StringBuilder item = new();
            lock (session.DiagnosticsLock)
            {
                item.AppendFormat("{0,9}:{1,20}:", reason, session.SessionDiagnostics.SessionName);
                if (lastContact)
                {
                    item.AppendFormat("Last Event:{0:HH:mm:ss}", session.SessionDiagnostics.ClientLastContactTime.ToLocalTime());
                }
                else
                {
                    if (session.Identity != null)
                    {
                        item.AppendFormat(":{0,20}", session.Identity.DisplayName);
                    }
                    item.AppendFormat(":{0}", session.Id);
                }
            }
            _logger.LogInformation(item.ToString());
        }
    }
}
