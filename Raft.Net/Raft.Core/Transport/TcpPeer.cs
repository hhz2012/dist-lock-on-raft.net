/* 
  Copyright (C) 2018 tiesky.com / Alex Solovyov
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Biser;
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Newtonsoft.Json;
//using Newtonsoft.Json;
using Raft.Core;
using Raft.Core.Transport;

namespace Raft.Transport
{
    public class TcpPeer : IDisposable
    {
        IChannelHandlerContext _nettyclient;
        IChannel clientChannel = null;
        //cSprot1Parser _sprot1 = null;
        RaftServiceNode trn = null;
        public TcpMsgHandshake Handshake = null;
        public NodeRaftAddress na = null;
        string _endPointSID = "";

        public TcpPeer(IChannelHandlerContext client, RaftServiceNode rn)
        {
            _nettyclient = client;
            trn = rn;
            trn.GetNode().timerLoop.TM.FireEventEach(10000, (o) =>
            {
            }, null, true);
        }
        /// <summary>
        /// Combination of remote (outgoing) ip and its local listening port
        /// </summary>
        public string EndPointSID
        {
            get
            {
                if (!String.IsNullOrEmpty(_endPointSID))
                    return _endPointSID;
                if (Handshake == null)
                    return String.Empty;
                string rep = null;
                if (this.clientChannel != null)
                {
                    rep = this.clientChannel.RemoteAddress.ToString();
                }
                if (this._nettyclient != null)
                {
                    rep = this._nettyclient.Channel.RemoteAddress.ToString();
                }
                _endPointSID = rep.Substring(0, rep.IndexOf(':') + 1) + Handshake.NodeListeningPort;
                return _endPointSID;
            }
        }

        public TcpPeer(string hostname, int port, RaftServiceNode rn)
        {
            trn = rn;
        }
        public  async Task Connect(string hostname, int port)
        {
            var group = new MultithreadEventLoopGroup();
            var bootstrap = new Bootstrap();
            bootstrap
            .Group(group)
            .Channel<TcpSocketChannel>()
            .Option(ChannelOption.TcpNodelay, true)
            .Handler(
                        new ActionChannelInitializer<ISocketChannel>(
                             channel =>
                             {
                                 IChannelPipeline pipeline = channel.Pipeline;
                                 pipeline.AddLast("framing-enc", new LengthFieldPrepender(2));
                                 pipeline.AddLast("framing-dec", new LengthFieldBasedFrameDecoder(ushort.MaxValue, 0, 2, 0, 2));
                                 pipeline.AddLast(new StringEncoder(), new StringDecoder());
                                 pipeline.AddLast("echo", new PeerMessageHandler(this));
                             }));
            IChannel clientChannel = await bootstrap.ConnectAsync(new IPEndPoint(IPAddress.Parse(hostname),port));
            this.clientChannel = clientChannel;
        }

        internal void FillNodeAddress()
        {
            na = new NodeRaftAddress() { NodeAddressId = Handshake.NodeListeningPort, NodeUId = Handshake.NodeUID, EndPointSID = this.EndPointSID };
        }

        private void packetParser(RaftCommand message)
        {
            try
            {
                switch (message.Code)
                {
                    case RaftCommand.Handshake: //Handshake

                        Handshake = message.Message as TcpMsgHandshake;
                        trn.peerNetwork.AddPeerToClusterEndPoints(this, true);
                        return;
                    case RaftCommand.RaftMessage: //RaftMessage

                        if (this.na == null)
                            return;
                        var msg = message.Message as TcpMsgRaft;
                        Task.Run(() =>
                        {
                            try
                            {
                                var node = trn.GetNode();
                                if (node != null)
                                {
                                    node.HandleRaftSignal(this.na, msg.RaftSignalType, msg.orginalObject);
                                }
                                else
                                {
                                    Console.WriteLine("cant find node");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex);
                            }
                        });
                        return;
                    case RaftCommand.HandshakeACK: //Handshake ACK
                        Handshake = message.Message as TcpMsgHandshake;
                        trn.peerNetwork.AddPeerToClusterEndPoints(this, false);
                        return;
                    case RaftCommand.FreeMessage: //Free Message protocol
                        var Tcpmsg = message.Message as TcpMsg;
                        if (na != null)
                        {
                            trn.log.Log(new WarningLogEntry()
                            {
                                LogType = WarningLogEntry.eLogType.DEBUG,
                                Description = $"{trn.port} ({trn.GetNode().NodeState})> peer {na.NodeAddressId} sent: { Tcpmsg.MsgType }"
                            });
                        }
                        return;
                    case RaftCommand.Ping: //Ping
                        return;
                }
            }
            catch (Exception ex)
            {
                Dispose();
            }
        }
        
        public void Send(int Command, IEncoder msg)
        {
            //RaftCommand cmd = new RaftCommand()
            //{
            //    Code = Command,
            //    Message = msg
            //};
            //var text = JsonConvert.SerializeObject(cmd, new JsonSerializerSettings()
            //{
            //    NullValueHandling = NullValueHandling.Ignore,
            //    TypeNameHandling = TypeNameHandling.All,
            //    TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Full
            //});

            //
            string str = $"{Command}," + Convert.ToBase64String(msg.BiserEncode());

            if (this._nettyclient != null)
            {
                this._nettyclient.WriteAndFlushAsync(str);
                //this._nettyclient.WriteAndFlushAsync();
            }
            else if (this.clientChannel != null)
            {
                this.clientChannel.WriteAndFlushAsync(str);
                //this.clientChannel.WriteAndFlushAsync(msg.BiserEncode());

            }
            else
            {
                Console.WriteLine("null connection");
            }
        }

        public async Task OnRecieve(IChannelHandlerContext context, string msgstr)
        {
            //
            if (!string.IsNullOrEmpty(msgstr))
            {
                //RaftCommand  msgObj = Newtonsoft.Json.JsonConvert.DeserializeObject<RaftCommand>(msgstr, new JsonSerializerSettings()
                //{
                //    NullValueHandling = NullValueHandling.Ignore,
                //    TypeNameHandling = TypeNameHandling.All,
                //    TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Full
                //}
                //);
                int index = msgstr.IndexOf(',');
                string num = msgstr.Substring(0, index);
                string base64 = msgstr.Substring(index + 1);
                byte[] data= Convert.FromBase64String(base64);
                RaftCommand cmd = new RaftCommand();
                cmd.Code = Convert.ToInt32(num);
                switch (cmd.Code)
                {
                    case RaftCommand.Handshake:
                        cmd.Message = TcpMsgHandshake.BiserDecode(data);
                        break;
                    case RaftCommand.HandshakeACK:
                        cmd.Message = TcpMsgHandshake.BiserDecode(data);
                        break;
                    case RaftCommand.RaftMessage:
                        cmd.Message = TcpMsgRaft.BiserDecode(data);
                        TcpMsgRaft t = (TcpMsgRaft)cmd.Message;
                        switch (t.RaftSignalType)
                        {
                            case eRaftSignalType.LeaderHearthbeat:
                                t.orginalObject = LeaderHeartbeat.BiserDecode(t.Data);
                                break;
                            case eRaftSignalType.CandidateRequest:
                                t.orginalObject = CandidateRequest.BiserDecode(t.Data);
                                break;
                            case eRaftSignalType.StateLogEntryAccepted:
                                t.orginalObject = StateLogEntryApplied.BiserDecode(t.Data);
                                break;
                            case eRaftSignalType.StateLogEntryRequest:
                                t.orginalObject = StateLogEntryRequest.BiserDecode(t.Data);
                                break;
                            case eRaftSignalType.StateLogEntrySuggestion:
                                t.orginalObject = StateLogEntrySuggestion.BiserDecode(t.Data);
                                break;
                            case eRaftSignalType.StateLogRedirectRequest:
                                t.orginalObject = StateLogEntryRedirectRequest.BiserDecode(t.Data);
                                break;
                            case eRaftSignalType.VoteOfCandidate:
                                t.orginalObject = VoteOfCandidate.BiserDecode(t.Data);
                                break;
                        }
                        break;
                    case RaftCommand.FreeMessage:
                        cmd.Message = TcpMsg.BiserDecode(data);
                        break;
                }
                this.packetParser(cmd as RaftCommand);
            }
        }

        long disposed = 0;

        public bool Disposed
        {
            get { return System.Threading.Interlocked.Read(ref disposed) == 1; }
        }

        /// <summary>
        /// all custom disposals via parametrical Dispose
        /// </summary>
        public void Dispose()
        {
            this.Dispose(false, true);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="DontRemoveFromSpider"></param>
        /// <param name="calledFromDispose"></param>
        public void Dispose(bool DontRemoveFromSpider, bool calledFromDispose = false)
        {
            if (System.Threading.Interlocked.CompareExchange(ref disposed, 1, 0) != 0)
                return;

            string endpoint = null;
            try
            {
                endpoint = this.EndPointSID;
            }
            catch (Exception ex)
            {
            }
           
            if (!DontRemoveFromSpider && endpoint != null)
                trn.peerNetwork.RemovePeerFromClusterEndPoints(endpoint);
            //-------------  Last line
            if (!calledFromDispose)
                Dispose();
        }
    }//eoc

  

}//eon
