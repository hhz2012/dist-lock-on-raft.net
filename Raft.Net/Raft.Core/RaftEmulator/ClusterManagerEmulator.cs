﻿/* 
  Copyright (C) 2018 tiesky.com / Alex Solovyov
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Raft;
using Raft.Transport;

namespace Raft.RaftEmulator
{
    public class ClusterManagerEmulator:IRaftComSender,IWarningLog
    {
        Dictionary<long, IEmulatedNode> nodes = new Dictionary<long, IEmulatedNode>();
        object sync_nodes = new object();
        List<TcpClusterEndPoint> eps = new List<TcpClusterEndPoint>();
        RaftEntitySettings re_settings = null;
        
        public void StartEmulateTcpNodes(int nodesQuantity)
        {
            TcpRaftNode trn = null;

          
                        
            for(int i = 0;i< nodesQuantity;i++)
                eps.Add(new TcpClusterEndPoint() { Host = "127.0.0.1", Port = 4250 + i });

            List<RaftEntitySettings> list = new List<RaftEntitySettings>();
           
                re_settings = new RaftEntitySettings()
                {
                    VerboseRaft = true,
                    //VerboseRaft = false,
                    VerboseTransport = false,

                    DelayedPersistenceIsActive = true,

                    //InMemoryEntity = true,
                    //InMemoryEntityStartSyncFromLatestEntity = true
                };
                //S:\temp\RaftDbr
                list.Add(re_settings);
           
                for (int i = 0; i < nodesQuantity; i++)
            {
                lock (sync_nodes)
                {
                    var nodeName = "entity" + (i + 1);
                    trn = new TcpRaftNode(new NodeSettings() { TcpClusterEndPoints = eps, RaftEntitiesSettings =  new List<RaftEntitySettings>() { re_settings } }, @"D:\Temp\RaftDBreeze\node" + (4250 + i),
                        (entityName, index, data,node) => { 
                            Console.WriteLine($"wow committed {entityName}/{index}; DataLen: {(data == null ? -1 : data.Length)}"); 
                            //handle shards operation
                            try
                            {
                                string str = System.Text.Encoding.Default.GetString(data);
                                if (str.StartsWith("shards:"))
                                {
                                    string json = str.Substring(7);
                                    if (json==node.NodeName)
                                    {
                                        //start a shard
                                        var shard = new ShardEmulator();
                                        shard.StartEmulateTcpNodes(3);
                                        this.Shards.Add(shard);
                                    }

                                }

                               // Console.WriteLine(str+" is received");
                            }
                            catch (Exception ex)
                            {

                            }
                            return true; },
                        4250 + i, nodeName, this);

                    //rn = new TcpRaftNode(eps, @"S:\temp\RaftDbr\node" + (4250 + i), 4250 + i,
                    //       (data) => {
                    //           Console.WriteLine($"wow committed");
                    //       }, this, rn_settings);

                    var item = trn.GetNodeByEntityName(re_settings.EntityName);
                    nodes.Add(item.NodeAddress.NodeAddressId, trn);
                    
                }

                trn.Start();

                //new Thread(() =>
                //{
                //    rn.Start();
                //    //Thread.CurrentThread.IsBackground = true;

                //    //lock (sync_nodes)
                //    //{
                //    //    rn = new TcpRaftNode(eps, 4250 + i, this, rn_settings);
                //    //    nodes.Add(rn.rn.NodeAddress.NodeAddressId, rn);
                //    //    rn.Start();
                //    //}
                    
                //}).Start();

                //Task.Run(() =>
                //{
                //    rn = new TcpRaftNode(eps, 4250 + i, this, rn_settings);
                //    lock (sync_nodes)
                //    {
                //        nodes.Add(rn.rn.NodeAddress.NodeAddressId, rn);
                //    }
                //    rn.Start();
                //});
                

                //rn.Verbose = true;
                //rn.SetNodesQuantityInTheCluster((uint)nodesQuantity);
                //rn.NodeAddress.NodeAddressId = i + 1;
                //lock (sync_nodes)
                //{
                //    nodes.Add(4250 + i, rn);
                //}
                 System.Threading.Thread.Sleep((new Random()).Next(30, 350));
                //// System.Threading.Thread.Sleep(500);
                //rn.NodeStart();
                //rn.Start();
            }
        }


        public void StartEmulateNodes(int nodesQuantity)
        {
            RaftNode rn =null;

            RaftEntitySettings re_settings = new RaftEntitySettings()
            {
                 
            };

            for (int i = 0; i < nodesQuantity; i++)
            {
                rn = new RaftNode(re_settings, new DBreeze.DBreezeEngine(@"D:\Temp\RaftDBreeze\node" + (4250 + i)), this, this,
                    (entityName, index, data,node) => { 
                        return true;
                    });
                //rn.Verbose = true;
                rn.SetNodesQuantityInTheCluster((uint)nodesQuantity);
                rn.NodeAddress.NodeAddressId = i + 1;
                lock (sync_nodes)
                {
                    nodes.Add(rn.NodeAddress.NodeAddressId, rn);
                }
                System.Threading.Thread.Sleep((new Random()).Next(30, 150));
               // System.Threading.Thread.Sleep(500);
                rn.NodeStart();
            }
        }

        /// <summary>
        /// Test method
        /// </summary>
        /// <param name="nodeId"></param>
        /// <param name="data"></param>
        public void SendData(int nodeId, string data, string entityName = "default")
        {
            Task.Run(() =>
            {
                lock (sync_nodes)
                {
                    if (nodes.Count < 1)
                        return;


                    if (nodes.First().Value is TcpRaftNode)
                    {
                        var leader = nodes.Where(r => ((TcpRaftNode)r.Value).GetNodeByEntityName(entityName) != null && ((TcpRaftNode)r.Value).GetNodeByEntityName(entityName).IsRunning && ((TcpRaftNode)r.Value).GetNodeByEntityName(entityName).NodeState == RaftNode.eNodeState.Leader)
                        .Select(r => (TcpRaftNode)r.Value).FirstOrDefault();

                        if (leader == null)
                            return;

                        ((TcpRaftNode)leader).AddLogEntry(System.Text.Encoding.UTF8.GetBytes(data));
                    }
                    else
                    {
                        var leader = nodes.Where(r => ((RaftNode)r.Value).IsRunning && ((RaftNode)r.Value).NodeState == RaftNode.eNodeState.Leader)
                        .Select(r => (RaftNode)r.Value).FirstOrDefault();

                        if (leader == null)
                            return;

                       ((RaftNode)leader).AddLogEntry(System.Text.Encoding.UTF8.GetBytes(data));
                    }
                }
            });

           
        }

        ///// <summary>
        ///// Test method
        ///// </summary>
        ///// <param name="nodeId"></param>
        ///// <param name="stateLogId"></param>
        ///// <returns></returns>
        //public bool ContainsStateLogIdData(int nodeId, ulong stateLogId)
        //{
        //    IEmulatedNode node = null;
        //    lock (sync_nodes)
        //    {
        //        nodes.TryGetValue(nodeId, out node);
        //    }
        //    //var node = nodes.Where(r => r.NodeAddress.NodeAddressId == nodeId).FirstOrDefault();
        //    if (node == null)
        //        return false;

        //    return ((RaftNode)node).ContainsStateLogEntryId(stateLogId);
        //}


        public void Start(int nodeId)
        {
            IEmulatedNode node = null;
            lock (sync_nodes)
            {
                nodes.TryGetValue(nodeId, out node);
            }
            //var node = nodes.Where(r => r.NodeAddress.NodeAddressId == nodeId).FirstOrDefault();
            if (node != null)
            {
                if (node is TcpRaftNode)
                {
                    if (!((TcpRaftNode)node).Disposed)
                        return;
                    node = null;

                    TcpRaftNode trn = null;

                    lock (sync_nodes)
                    {
                        trn = new TcpRaftNode(new NodeSettings() { TcpClusterEndPoints = eps, RaftEntitiesSettings = new List<RaftEntitySettings> { re_settings } }, @"D:\Temp\RaftDBreeze\node"+ nodeId,
                            (entityName, index, data,node) => { Console.WriteLine($"wow committed {entityName}/{index}; DataLen: {(data == null ? -1 : data.Length)}"); return true; },
                            nodeId,  null,this);
                        nodes[trn.GetNodeByEntityName("default").NodeAddress.NodeAddressId] = trn;
                    }
                    trn.Start();
                }
                else
                {
                    node.EmulationStart();
                }
            }
        }

        public void Stop(int nodeId)
        {
            IEmulatedNode node = null;
            lock (sync_nodes)
            {
                nodes.TryGetValue(nodeId, out node);
                
            }
            //var node = nodes.Where(r => r.NodeAddress.NodeAddressId == nodeId).FirstOrDefault();
            if (node != null)
            {
                if (node is TcpRaftNode)
                {
                    if (((TcpRaftNode)node).Disposed)
                        return;

                    ((TcpRaftNode)node).Dispose();

                    lock (sync_nodes)
                    {
                        //nodes[nodeId] = null;
                    }
                    
                    node = null;
                }
                else
                {
                    node.EmulationStop();
                }
            }
        }
       
        public void SendTestAll(int nodeId)
        {
            IEmulatedNode node = null;
            lock (sync_nodes)
            {
                nodes.TryGetValue(nodeId, out node);
            }
            //var node = nodes.Where(r => r.NodeAddress.NodeAddressId == nodeId).FirstOrDefault();
            if (node != null)
                node.EmulationSendToAll();
        }
        public List<ShardEmulator> Shards { get; set; } = new List<ShardEmulator>();

        #region "IRaftComSender"

        public void SetValue(byte[] data, string entityName="default")
        {
         
            Task.Run(() =>
            {
                lock (sync_nodes)
                {
                    if (nodes.Count < 1)
                        return;


                    if (nodes.First().Value is TcpRaftNode)
                    {
                        var leader = nodes.Where(r => ((TcpRaftNode)r.Value).GetNodeByEntityName(entityName) != null && ((TcpRaftNode)r.Value).GetNodeByEntityName(entityName).IsRunning && ((TcpRaftNode)r.Value).GetNodeByEntityName(entityName).NodeState == RaftNode.eNodeState.Leader)
                        .Select(r => (TcpRaftNode)r.Value).FirstOrDefault();

                        if (leader == null)
                            return;

                        leader.AddLogEntry(data, entityName);
                    }
                    else
                    {
                        var leader = nodes.Where(r => ((RaftNode)r.Value).IsRunning && ((RaftNode)r.Value).NodeState == RaftNode.eNodeState.Leader)
                        .Select(r => (RaftNode)r.Value).FirstOrDefault();

                        if (leader == null)
                            return;

                        leader.AddLogEntry(data);
                    }
                }
            });

        }

        public void SendToAll(eRaftSignalType signalType, byte[] data, NodeAddress myNodeAddress, string entityName, bool highPriority = false)
        {
            Task.Run(() =>
                {
                    lock (sync_nodes)
                    {
                        foreach (var n in nodes)
                        {
                            if (!((RaftNode)n.Value).IsRunning)
                                continue;

                            if (((RaftNode)n.Value).NodeAddress.NodeAddressId == myNodeAddress.NodeAddressId)
                                continue;       //Skipping sending to self

                            //May be put it all into new Threads or so !! no for udp channels
                            ((IRaftComReceiver)n.Value).IncomingSignalHandler(myNodeAddress, signalType, data);
                        }
                    }
                });
            
        }

        public void SendTo(NodeAddress nodeAddress, eRaftSignalType signalType, byte[] data, NodeAddress myNodeAddress, string entityName)
        {
            Task.Run(() =>
            {
                lock (sync_nodes)
                {
                    foreach (var n in nodes)
                    {
                        if (!((RaftNode)n.Value).IsRunning)
                            continue;

                        if (((RaftNode)n.Value).NodeAddress.NodeAddressId == myNodeAddress.NodeAddressId)
                            continue;       //Skipping sending to self

                        if (((RaftNode)n.Value).NodeAddress.NodeAddressId == nodeAddress.NodeAddressId)
                        {
                            //May be put it all into new Threads or so
                            ((IRaftComReceiver)n.Value).IncomingSignalHandler(myNodeAddress, signalType, data);

                            break;
                        }
                    }
                }
            });
        }

        
        #endregion

        string logFn = @"D:\Temp\x1\log.txt";
        System.IO.StreamWriter sw = null;
        #region "IWarningLog"
        public void Log(WarningLogEntry logEntry)
        {
            //if (sw == null)
            //    sw = new System.IO.StreamWriter(logFn);

            //sw.WriteLine(logEntry.Description);
            //sw.Flush();
            Console.WriteLine(logEntry.Description);
            
            //throw new NotImplementedException();
        }
        #endregion
    }
}