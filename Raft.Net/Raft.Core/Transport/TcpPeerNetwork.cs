/* 
  Copyright (C) 2018 tiesky.com / Alex Solovyov
  It's a free software for those, who think that it should be free.
*/
using Biser;
using DotNetty.Transport.Channels;
using Raft.Core;
using Raft.Core.Transport;
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
    public  class TcpPeerNetwork : IPeerConnector, IDisposable
    {      
        ReaderWriterLockSlim _sync = new ReaderWriterLockSlim();
        Dictionary<string,TcpPeer> Peers = new Dictionary<string, TcpPeer>();     //Key - NodeUID    
        internal RaftServiceNode trn = null;
        TcpPeerListener listener = null;
        public TcpPeerNetwork(RaftServiceNode trn)
        {
            this.trn = trn;
            
        }
        public void Start()
        {
            this.listener = new TcpPeerListener(this, trn.port);
            this.listener.DoTcpServer();
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
                        peer.Send(RaftCommand.HandshakeACK,new TcpMsgHandshake()
                        {
                            NodeListeningPort = trn.port,
                            NodeUID = trn.GetNode().NodeAddress.NodeUId,
                        });
                    }
                }
                else
                {
                    //Peers[peer.EndPointSID].Send(RaftCommand.Ping,"ping"); //ping
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
            trn.GetNode().timerLoop.TM.FireEventEach(3000, RetestConnections, null, false);
        }
        public bool IsMe(PeerEndPoint endPoint)
        {
            if (this.trn.port == endPoint.Port) return true;
            return false;
        }
        async Task HandshakeTo(List<PeerEndPoint> clusterEndPoints)        
        {
            foreach (var el in clusterEndPoints)
            {
                try
                {
                    if (this.IsMe(el))
                    {
                        continue;
                    }
                    el.Peer = new TcpPeer(el.Host, el.Port,this.trn);
                    await el.Peer.Connect(el.Host, el.Port);
                    el.Peer.Send(RaftCommand.Handshake, new TcpMsgHandshake()
                    {
                        NodeListeningPort = trn.port,
                        NodeUID = trn.GetNode().NodeAddress.NodeUId, //Generated GUID on Node start                        

                     });
                    if (GlobalConfig.DebugNetwork)
                    {
                        var localname = this.trn.NodeName;
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
                if (!trn.GetNode().States.IsRunning)
                    return;
                List<TcpPeer> peers = GetPeers();
                
                if (peers.Count == trn.NodeSettings.TcpClusterEndPoints.Count - 1)
                    return;

                var list2Lookup = new HashSet<string>(peers.Select(r => r?.EndPointSID));
                var ws = trn.NodeSettings.TcpClusterEndPoints.Where(r => (!list2Lookup.Contains(r.EndPointSID))).ToList();

                if (ws.Count > 0)
                {                    
                    await HandshakeTo(ws);
                }
            }
            catch (Exception ex)
            {

            }
        }
        public void SendToAll(eRaftSignalType signalType, IEncoder data, NodeRaftAddress senderNodeAddress, string entityName, bool highPriority = false)
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
                    peer.Send(RaftCommand.RaftMessage,new TcpMsgRaft() { EntityName = entityName, RaftSignalType = signalType, Data = data.BiserEncode() });
                }
            }
            catch (Exception ex)
            {
                
            }
        }

        public void SendTo(NodeRaftAddress nodeAddress, eRaftSignalType signalType, IEncoder data, NodeRaftAddress senderNodeAddress, string entityName)
        {
            try
            {
                TcpPeer peer = null;
                if (Peers.TryGetValue(nodeAddress.EndPointSID, out peer))
                {
                    peer.Send(RaftCommand.RaftMessage,new TcpMsgRaft() { EntityName = entityName, RaftSignalType = signalType, Data = data.BiserEncode() });
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
