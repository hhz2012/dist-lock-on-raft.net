/* 
  Copyright (C) 2018 tiesky.com / Alex Solovyov
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Newtonsoft.Json;

namespace Raft.Transport
{
    public class TcpPeer : IDisposable
    {
        IChannelHandlerContext _nettyclient;
        IChannel clientChannel = null;
        //cSprot1Parser _sprot1 = null;
        RaftNode trn = null;
        public TcpMsgHandshake Handshake = null;
        public NodeRaftAddress na = null;
        string _endPointSID = "";

        public TcpPeer(IChannelHandlerContext client, RaftNode rn)
        {
            _nettyclient = client;
            trn = rn;
            trn.GetNodeByEntityName("default").TM.FireEventEach(10000, (o) =>
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

        public TcpPeer(string hostname, int port, RaftNode rn)
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

                                pipeline.AddLast("echo", new EchoClientHandler(this));
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
                    case 1: //Handshake

                        Handshake = message.Message as TcpMsgHandshake;
                        trn.spider.AddPeerToClusterEndPoints(this, true);
                        return;
                    case 2: //RaftMessage

                        if (this.na == null)
                            return;
                        var msg = message.Message as TcpMsgRaft;
                        Task.Run(() =>
                        {
                            try
                            {
                                var node = trn.GetNodeByEntityName(msg.EntityName);
                                if (node != null)
                                {
                                    node.IncomingSignalHandler(this.na, msg.RaftSignalType, msg.Data);
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
                    case 3: //Handshake ACK

                        Handshake = message.Message as TcpMsgHandshake;
                        trn.spider.AddPeerToClusterEndPoints(this, false);
                        return;
                    case 4: //Free Message protocol

                        var Tcpmsg = message.Message as TcpMsg;
                        if (na != null)
                        {
                            trn.log.Log(new WarningLogEntry()
                            {
                                LogType = WarningLogEntry.eLogType.DEBUG,
                                Description = $"{trn.port} ({trn.GetNodeByEntityName("default").NodeState})> peer {na.NodeAddressId} sent: { Tcpmsg.MsgType }"
                            });
                        }
                        return;
                    case 5: //Ping
                        return;
                }
            }
            catch (Exception ex)
            {
                Dispose();
            }
        }
        
        public void Send(int Command,object msg)
        {
            RaftCommand cmd = new RaftCommand()
            {
                Code = Command,
                Message = msg
            };
            var text = Newtonsoft.Json.JsonConvert.SerializeObject(cmd, new JsonSerializerSettings()
            {
                NullValueHandling = NullValueHandling.Ignore,
                TypeNameHandling = TypeNameHandling.All,
                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Full
            });

           // var data = System.Text.Encoding.UTF8.GetBytes("hello world");
            if (this._nettyclient != null)
                this._nettyclient.WriteAndFlushAsync(text);
            else if (this.clientChannel != null)
            {
                this.clientChannel.WriteAndFlushAsync(text);
                
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
                RaftCommand  msgObj = Newtonsoft.Json.JsonConvert.DeserializeObject<RaftCommand>(msgstr, new JsonSerializerSettings()
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    TypeNameHandling = TypeNameHandling.All,
                    TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Full
                }
                );
                this.packetParser(msgObj as RaftCommand);
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
                trn.spider.RemovePeerFromClusterEndPoints(endpoint);
            //-------------  Last line
            if (!calledFromDispose)
                Dispose();
        }
    }//eoc

    public class EchoClientHandler : ChannelHandlerAdapter
    {
        readonly IByteBuffer initialMessage;
        TcpPeer peer = null;
        public EchoClientHandler(TcpPeer peer)
        {
            this.peer = peer;
           // this.initialMessage = Unpooled.Buffer(1000);
          //  byte[] messageBytes = Encoding.UTF8.GetBytes("Hello world");
           // this.initialMessage.WriteBytes(messageBytes);
        }


        public override void ChannelActive(IChannelHandlerContext context) => context.WriteAndFlushAsync(this.initialMessage);

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            
//               Console.WriteLine("Received from server: " + message);
            this.peer.OnRecieve(context, message as string).ConfigureAwait(false).GetAwaiter().GetResult();
            
        }

        public override void ChannelReadComplete(IChannelHandlerContext context) => context.Flush();

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            Console.WriteLine("Exception: " + exception);
            context.CloseAsync();
        }
    }
    public class RaftCommand
    {
        public int Code { get; set; }
        public object Message { get; set; }
    }
}//eon
