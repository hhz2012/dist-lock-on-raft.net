/* 
  Copyright (C) 2018 tiesky.com / Alex Solovyov
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using DBreeze;
using DBreeze.Utils;

using Raft.Core.Handler;

namespace Raft.Transport
{
    public class RaftServiceNode: IDisposable
    {       
        internal IWarningLog log = null;
        internal int port = 0;
        internal RaftStateMachine raftNode = null;
        internal TcpPeerNetwork peerNetwork = null;
        internal NodeSettings NodeSettings = null;
        public string NodeName { get; set; }
        
        public RaftServiceNode(NodeSettings nodeSettings, string dbreezePath, IActionHandler handler, int port = 4250, string nodeName="default", IWarningLog log = null)
        {
            if (nodeSettings == null)
                nodeSettings = new NodeSettings();
            this.NodeSettings = nodeSettings;
            this.NodeName = nodeName;
            this.log = log;
            this.port = port;
         
            peerNetwork = new TcpPeerNetwork(this);
            //bool firstNode = true;
            if (this.NodeSettings.RaftEntitiesSettings == null)
            {
                this.NodeSettings.RaftEntitiesSettings = new RaftEntitySettings();
            }
            var re_settings = this.NodeSettings.RaftEntitiesSettings;

            if (String.IsNullOrEmpty(re_settings.EntityName))
                throw new Exception("Raft.Net: entities must have unique names. Change RaftNodeSettings.EntityName.");

            var rn = new RaftStateMachine(re_settings ?? new RaftEntitySettings(), dbreezePath, this.peerNetwork, this.log, handler);
             
            rn.SetNodesQuantityInTheCluster((uint)this.NodeSettings.TcpClusterEndPoints.Count);     
            rn.NodeAddress.NodeAddressId = port; //for debug/emulation purposes

            rn.NodeAddress.NodeUId = Guid.NewGuid().ToByteArray().Substring(8, 8).To_Int64_BigEndian();
            rn.NodeName = this.NodeName;
            this.raftNode = rn;
            rn.NodeStart();
            
        }

        /// <summary>
        /// Gets raft node by entity and returns if it is a leader 
        /// </summary>
        /// <param name="entityName"></param>
        /// <returns></returns>
        public bool IsLeader(string entityName = "default")
        {
            return this.raftNode.IsLeader;
            
        }
        public void Start()
        {
            this.peerNetwork.Start();
        }
        internal void PeerIsDisconnected(string endpointsid)
        {
           this.raftNode.PeerIsDisconnected(endpointsid);
        }
        public RaftStateMachine GetNode()
        {
            return this.raftNode;
            
        }
        public async Task StartConnect()
        {
            await peerNetwork.Handshake();
        }
        public bool NodeIsInLatestState()
        {
            RaftStateMachine rn = this.raftNode;
            return rn.NodeIsInLatestState;
            
        }
        /// <summary>
        /// called by external service
        /// </summary>
        /// <param name="data"></param>
        /// <param name="entityName"></param>
        /// <param name="timeoutMs"></param>
        /// <returns></returns>
               
        public async Task<bool> AddLogEntryRequestAsync(byte[] data, string entityName = "default", int timeoutMs = 20000)
        {
            if (System.Threading.Interlocked.Read(ref disposed) == 1)
                return false;

            RaftStateMachine rn = this.raftNode; ;
            {
                //Generating externalId
                var msgId = AsyncResponseHandler.GetMessageId();
                var msgIdStr = msgId.ToBytesString();
                var resp = new ResponseCrate();
                resp.TimeoutsMs = timeoutMs; //enable for amre
                resp.Init_AMRE();
                AsyncResponseHandler.df[msgIdStr] = resp;
                var aler = rn.logHandler.AddLogEntryRequest(data,msgId);
                switch(aler.AddResult)
                {
                    case AddLogEntryResult.eAddLogEntryResult.LOG_ENTRY_IS_CACHED:
                    case AddLogEntryResult.eAddLogEntryResult.NODE_NOT_A_LEADER:
                        //async waiting
                        resp.amre.Wait();    //enable for amre
                        resp.Dispose_MRE();
                        if (AsyncResponseHandler.df.TryRemove(msgIdStr, out resp))
                        {
                           if (resp.IsRespOk)
                                return true;
                        }
                        break;
                    default:
                        resp.Dispose_MRE();
                        AsyncResponseHandler.df.TryRemove(msgIdStr, out resp);
                        return false;
                }
            }
            return false;
        }
        long disposed = 0;
        public bool Disposed
        {
            get { return System.Threading.Interlocked.Read(ref disposed) == 1; }
        }
        public void Dispose()
        {
            if (System.Threading.Interlocked.CompareExchange(ref disposed, 1, 0) != 0)
                return;
            try
            {
                this.raftNode = null;
            }
            catch (Exception ex)
            {
            }
            try
            {
                if (peerNetwork != null)
                {
                    peerNetwork.Dispose();
                }
            }
            catch (Exception ex)
            {

            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="entityName"></param>
        public void Debug_PrintOutInMemory(string entityName = "default")
        {
            RaftStateMachine rn = this.raftNode;
            rn.Debug_PrintOutInMemory();
        }
    }//eo class
}//eo namespace
