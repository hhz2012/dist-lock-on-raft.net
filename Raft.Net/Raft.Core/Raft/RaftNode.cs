/* 
  Copyright (C) 2018 tiesky.com / Alex Solovyov
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using DBreeze;
using DBreeze.Utils;
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Raft.Core.Handler;

namespace Raft.Transport
{
    public class RaftNode: IDisposable
    {       
        internal IWarningLog log = null;
        internal int port = 0;
        internal Dictionary<string, RaftStateMachine> raftNodes = new Dictionary<string, RaftStateMachine>();
        internal TcpPeerNetwork spider = null;
        internal DBreezeEngine dbEngine;
        internal NodeSettings NodeSettings = null;
        internal string nodeName;
        public string NodeName
        {
            get
            {
                return this.nodeName;
            }
        }
        public RaftNode(NodeSettings nodeSettings, string dbreezePath, IActionHandler handler, int port = 4250, string nodeName="default", IWarningLog log = null)
        {
            if (nodeSettings == null)
                nodeSettings = new NodeSettings();
            this.NodeSettings = nodeSettings;
            this.nodeName = nodeName;
            this.log = log;
            this.port = port;

            DBreezeConfiguration conf = new DBreezeConfiguration()
            {
                DBreezeDataFolderName = dbreezePath
            };

            if (nodeSettings.RaftEntitiesSettings.Where(MyEnt => !MyEnt.InMemoryEntity).Count() == 0)
            {
                conf.Storage = DBreezeConfiguration.eStorage.MEMORY;
            }
            else
            {
                conf.Storage = DBreezeConfiguration.eStorage.DISK;
            }
            conf.AlternativeTablesLocations.Add("mem_*", String.Empty);

            dbEngine = new DBreezeEngine(conf);

            spider = new TcpPeerNetwork(this);

            //bool firstNode = true;
            if (this.NodeSettings.RaftEntitiesSettings == null)
            {
                this.NodeSettings.RaftEntitiesSettings = new List<RaftEntitySettings>();
            }

            if(this.NodeSettings.RaftEntitiesSettings.Where(r=>r.EntityName.ToLower() == "default").Count()<1)
                this.NodeSettings.RaftEntitiesSettings.Add(new RaftEntitySettings());
            foreach (var re_settings in this.NodeSettings.RaftEntitiesSettings)
            {
                if (String.IsNullOrEmpty(re_settings.EntityName))
                    throw new Exception("Raft.Net: entities must have unique names. Change RaftNodeSettings.EntityName.");

                if (this.raftNodes.ContainsKey(re_settings.EntityName))
                    throw new Exception("Raft.Net: entities must have unique names. Change RaftNodeSettings.EntityName.");

                var rn = new RaftStateMachine(re_settings ?? new RaftEntitySettings(), this.dbEngine, this.spider, this.log, handler);
             
                rn.Verbose = re_settings.VerboseRaft;       
                rn.SetNodesQuantityInTheCluster((uint)this.NodeSettings.TcpClusterEndPoints.Count);     
                rn.NodeAddress.NodeAddressId = port; //for debug/emulation purposes

                rn.NodeAddress.NodeUId = Guid.NewGuid().ToByteArray().Substring(8, 8).To_Int64_BigEndian();
                rn.NodeName = this.NodeName;
                this.raftNodes[re_settings.EntityName] = rn;

                rn.NodeStart();
            }
        }

        /// <summary>
        /// Gets raft node by entity and returns if it is a leader 
        /// </summary>
        /// <param name="entityName"></param>
        /// <returns></returns>
        public bool IsLeader(string entityName = "default")
        {
            if(this.raftNodes.TryGetValue(entityName, out var rn))
            {
                return rn.IsLeader;
            }
            return false;
        }
        public void Start()
        {
            this.spider.Start();
        }
        internal void PeerIsDisconnected(string endpointsid)
        {
            foreach(var rn in this.raftNodes)
                rn.Value.PeerIsDisconnected(endpointsid);
        }

        public RaftStateMachine GetNodeByEntityName(string entityName)
        {
            RaftStateMachine rn = null;
            raftNodes.TryGetValue(entityName, out rn);
            return rn;
        }
        public async Task StartConnect()
        {
            await spider.Handshake();
        }

       
        public bool NodeIsInLatestState(string entityName = "default")
        {
            RaftStateMachine rn = null;
            if (this.raftNodes.TryGetValue(entityName, out rn))
                return rn.NodeIsInLatestState;

            return false;
        }

        public AddLogEntryResult AddLogEntry(byte[] data, string entityName = "default")
        {
            RaftStateMachine rn = null;
            var msgId = AsyncResponseHandler.GetMessageId();
            if (this.raftNodes.TryGetValue(entityName, out rn))
                return rn.AddLogEntry(data,msgId);

            return new AddLogEntryResult { AddResult = AddLogEntryResult.eAddLogEntryResult.NODE_NOT_FOUND_BY_NAME };
        }
               
        public async Task<bool> AddLogEntryAsync(byte[] data, string entityName = "default", int timeoutMs = 20000)
        {
            if (System.Threading.Interlocked.Read(ref disposed) == 1)
                return false;

            RaftStateMachine rn = null;
            if (this.raftNodes.TryGetValue(entityName, out rn))
            {
                //Generating externalId
                var msgId = AsyncResponseHandler.GetMessageId();
                var msgIdStr = msgId.ToBytesString();
                var resp = new ResponseCrate();
                resp.TimeoutsMs = timeoutMs; //enable for amre
                resp.Init_AMRE();
                AsyncResponseHandler.df[msgIdStr] = resp;
                var aler = rn.AddLogEntry(data,msgId);
                switch(aler.AddResult)
                {
                    case AddLogEntryResult.eAddLogEntryResult.LOG_ENTRY_IS_CACHED:
                    case AddLogEntryResult.eAddLogEntryResult.NODE_NOT_A_LEADER:
                        //async waiting
                        await resp.amre.WaitAsync();    //enable for amre
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
                foreach (var rn in this.raftNodes)
                {
                    rn.Value.Dispose();
                }
                this.raftNodes.Clear();
            }
            catch (Exception ex)
            {
            }
            try
            {
                if (spider != null)
                {
                    spider.Dispose();
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
            RaftStateMachine rn = null;
            if (this.raftNodes.TryGetValue(entityName, out rn))
                rn.Debug_PrintOutInMemory();
        }
    }//eo class

   
}//eo namespace
