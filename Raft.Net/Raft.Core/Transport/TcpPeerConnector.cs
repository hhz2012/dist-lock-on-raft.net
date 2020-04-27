/* 
  Copyright (C) 2018 tiesky.com / Alex Solovyov
  It's a free software for those, who think that it should be free.
*/
using DotNetty.Transport.Channels;
using Raft.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Raft.Transport
{
    /// <summary>
    /// Spider manages connections for all listed nodes of the net
    /// </summary>
    public  class TcpPeerConnector : IPeerConnector, IDisposable
    {      
        ReaderWriterLockSlim _sync = new ReaderWriterLockSlim();
        Dictionary<string,TcpPeer> Peers = new Dictionary<string, TcpPeer>();     //Key - NodeUID    
        internal TcpRaftNode trn = null;
        public TcpPeerConnector(TcpRaftNode trn)
        {
            this.trn = trn;          
        }
        public TcpPeer AddTcpClient(IChannelHandlerContext peer)
        {
            var p = new TcpPeer(peer, trn);
            return p;
        }
        public void RemoveAll()
        {
            var lst = Peers.ToList();
            _sync.EnterWriteLock();
            try
            {
                
                Peers.Clear();
            }
            catch (Exception ex)
            {
                //throw;
            }
            finally
            {
                _sync.ExitWriteLock();
            }

            foreach (var peer in lst)
            {
                try
                {
                    if (peer.Value != null)
                    {                        
                        peer.Value.Dispose(true);
                    }
                }
                catch
                {
                }
            }
        }

        internal void RemovePeerFromClusterEndPoints(string endpointsid)
        {
            if (String.IsNullOrEmpty(endpointsid))
                return;

            _sync.EnterWriteLock();
            try
            {
                Peers.Remove(endpointsid);
            }
            catch (Exception ex)
            {
                //throw;
            }
            finally
            {
                _sync.ExitWriteLock();
            }

            this.trn.PeerIsDisconnected(endpointsid);
        }

        public void AddPeerToClusterEndPoints(TcpPeer peer, bool handshake)
        {
            _sync.EnterWriteLock();
            try
            {

                //Choosing priority connection
                if (!Peers.ContainsKey(peer.EndPointSID))
                {                 
                    Peers[peer.EndPointSID] = peer;
                    peer.FillNodeAddress();

                    if (handshake)
                    {
                        //sending back handshake ack

                        //peer.Write(
                        //    cSprot1Parser.GetSprot1Codec(
                        //    new byte[] { 00, 03 }, (new TcpMsgHandshake()
                        //    {
                        //        NodeListeningPort = trn.port,
                        //        NodeUID = trn.GetNodeByEntityName("default").NodeAddress.NodeUId,
                        //    }).SerializeBiser())
                        //);
                        peer.Send(3,new TcpMsgHandshake()
                        {
                            NodeListeningPort = trn.port,
                            NodeUID = trn.GetNodeByEntityName("default").NodeAddress.NodeUId,
                        });
                    }
                }
                else
                {
                    //Peers[peer.EndPointSID].Write(cSprot1Parser.GetSprot1Codec(new byte[] { 00, 05 }, null)); //ping

                    Peers[peer.EndPointSID].Send(5,"ping"); //ping

                    //removing incoming connection                    
                    peer.Dispose(true);

                    return;
                }
            }
            catch (Exception ex)
            {
                //throw;
            }
            finally
            {
                _sync.ExitWriteLock();
            }

        }

        public async Task Handshake()        
        {
            await HandshakeTo(trn.NodeSettings.TcpClusterEndPoints);
            trn.GetNodeByEntityName("default").TM.FireEventEach(3000, RetestConnections, null, false);
        }
        public bool IsMe(TcpClusterEndPoint endPoint)
        {
            if (this.trn.port == endPoint.Port) return true;
            return false;
        }
        async Task HandshakeTo(List<TcpClusterEndPoint> clusterEndPoints)        
        {
            foreach (var el in clusterEndPoints)
            {
                try
                {
                    //TcpClient cl = new TcpClient();
                    if (this.IsMe(el))
                    {
                        continue;
                    }
                   // await cl.ConnectAsync(el.Host, el.Port);
                    el.Peer = new TcpPeer(el.Host, el.Port,this.trn);
                    await el.Peer.Connect(el.Host, el.Port);

                    //el.Peer.Write(cSprot1Parser.GetSprot1Codec(new byte[] { 00, 01 }, (new TcpMsgHandshake()                    
                    //{
                    //    NodeListeningPort = trn.port,
                    //    NodeUID = trn.GetNodeByEntityName("default").NodeAddress.NodeUId, //Generated GUID on Node start                        

                    // }).SerializeBiser()));

                    el.Peer.Send(1,new TcpMsgHandshake()
                    {
                        NodeListeningPort = trn.port,
                        NodeUID = trn.GetNodeByEntityName("default").NodeAddress.NodeUId, //Generated GUID on Node start                        

                     });
                    if (GlobalConfig.DebugNetwork)
                    {
                        var localname = this.trn.nodeName;
                        Console.WriteLine($" send handshake from {localname} to {el.Port}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  handshake eror"+ex.Message);
                }
            }
        }

        public List<TcpPeer> GetPeers(bool useLock = true)
        {
            List<TcpPeer> peers = null;

            if (useLock)
            {
                _sync.EnterReadLock();
                try
                {
                    peers = Peers.Values.ToList();
                }
                catch (Exception ex)
                { }
                finally
                {
                    _sync.ExitReadLock();
                }
            }
            else
                peers = Peers.Values.ToList();
            return peers ?? new List<TcpPeer>();
        }

        void RetestConnections(object obj)
        {
            RetestConnectionsAsync();
        }

        async Task RetestConnectionsAsync()        
        {
            try
            {
                if (!trn.GetNodeByEntityName("default").IsRunning)
                    return;

                List<TcpPeer> peers = GetPeers();
                
                if (peers.Count == trn.NodeSettings.TcpClusterEndPoints.Count - 1)
                    return;

                var list2Lookup = new HashSet<string>(peers.Select(r => r?.EndPointSID));
                var ws = trn.NodeSettings.TcpClusterEndPoints.Where(r => !r.Me && (!list2Lookup.Contains(r.EndPointSID))).ToList();

                if (ws.Count > 0)
                {                    
                    await HandshakeTo(ws);
                }

            }
            catch (Exception ex)
            {

            }
        }

        public void SendToAll(eRaftSignalType signalType, byte[] data, NodeAddress senderNodeAddress, string entityName, bool highPriority = false)
        {
            try
            {
                List<TcpPeer> peers = null;
                _sync.EnterReadLock();
                try
                {
                    peers = Peers.Values.ToList();
                }
                catch (Exception ex)
                {
                    //throw;
                }
                finally
                {
                    _sync.ExitReadLock();
                }

                foreach (var peer in peers)
                {

                    //peer.Write(cSprot1Parser.GetSprot1Codec(new byte[] { 00, 02 },
                    //    (
                    //        new TcpMsgRaft() { EntityName = entityName, RaftSignalType = signalType, Data = data }
                    //    ).SerializeBiser()), highPriority);

                    peer.Send(2,new TcpMsgRaft() { EntityName = entityName, RaftSignalType = signalType, Data = data });
                }
            }
            catch (Exception ex)
            {
                
            }
        }

        public void SendTo(NodeAddress nodeAddress, eRaftSignalType signalType, byte[] data, NodeAddress senderNodeAddress, string entityName)
        {
            try
            {
                TcpPeer peer = null;
                if (Peers.TryGetValue(nodeAddress.EndPointSID, out peer))
                {
                    //peer.Write(cSprot1Parser.GetSprot1Codec(new byte[] { 00, 02 },
                    //   (
                    //       new TcpMsgRaft() { EntityName = entityName, RaftSignalType = signalType, Data = data }
                    //   ).SerializeBiser()));
                    peer.Send(2,new TcpMsgRaft() { EntityName = entityName, RaftSignalType = signalType, Data = data });
                }
            }
            catch (Exception ex)
            {


            }
        }

        public void SendToAllFreeMessage(string msgType, string dataString="", byte[] data=null, NodeAddress senderNodeAddress = null)
        {
            try
            {
                List<TcpPeer> peers = null;
                _sync.EnterReadLock();
                try
                {
                    peers = Peers.Values.ToList();
                }
                catch (Exception ex)
                {
                    //throw;
                }
                finally
                {
                    _sync.ExitReadLock();
                }

                foreach (var peer in peers)
                {
                    //peer.Write(cSprot1Parser.GetSprot1Codec(new byte[] { 00, 04 },
                    //    (
                    //        new TcpMsg() { DataString = dataString, MsgType = msgType, Data = data }
                    //    ).SerializeBiser()));
                    peer.Send(4,new TcpMsg() { DataString = dataString, MsgType = msgType, Data = data }
                        );
                }

            }
            catch (Exception ex)
            {
                
            }
           
        }

        int disposed = 0;
        public void Dispose()
        {
            if (System.Threading.Interlocked.CompareExchange(ref disposed, 1, 0) != 0)
                return;

            RemoveAll();
        }
    }

}//eo namespace
