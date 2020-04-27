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
using System.Threading.Tasks;

using DBreeze.Utils;
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
        TcpClient _client;
        NetworkStream stream = null;
        IChannelHandlerContext _nettyclient;
        IChannel clientChannel = null;
        cSprot1Parser _sprot1 = null;
        TcpRaftNode trn = null;
        public TcpMsgHandshake Handshake = null;
        public NodeAddress na = null;
        string _endPointSID = "";

        //public TcpPeer(TcpClient client, TcpRaftNode rn)
        //{
        //    _client = client;
        //    trn = rn;
        //    try
        //    {
        //        stream = _client.GetStream();
        //        SetupSprot();
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine("can't connect to peer");
        //        return;
        //    }

        //    trn.GetNodeByEntityName("default").TM.FireEventEach(10000, (o) =>
        //    {
        //        //don't know why
        //        //if (Handshake == null)
        //        //this.Dispose();

        //    }, null, true);

        //    Task.Run(async () => await Read());
        //}
        public TcpPeer(IChannelHandlerContext client, TcpRaftNode rn)
        {
            _nettyclient = client;
            trn = rn;
            try
            {
                SetupSprot();
            }
            catch (Exception ex)
            {
                Console.WriteLine("can't connect to peer");
                return;
            }

            trn.GetNodeByEntityName("default").TM.FireEventEach(10000, (o) =>
            {
                //don't know why
                //if (Handshake == null)
                //this.Dispose();

            }, null, true);

            Task.Run(async () => await Read());
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
                var rep = _client.Client.RemoteEndPoint.ToString();
                _endPointSID = rep.Substring(0, rep.IndexOf(':') + 1) + Handshake.NodeListeningPort;
                return _endPointSID;
            }
        }

        public TcpPeer(string hostname, int port, TcpRaftNode rn)
        {
            trn = rn;
            //Task.Run(async () => await Connect(hostname, port));
        }

        //async Task Connect(string hostname, int port)
        //{
        //    _client = new TcpClient();
        //    try
        //    {
        //        await _client.ConnectAsync(hostname, port);
        //        stream = _client.GetStream();
        //        SetupSprot();
        //    }
        //    catch (Exception ex)
        //    {
        //        return;
        //    }

        //    await Read();
        //}
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
                              //   pipeline.AddLast(new LoggingHandler());
                                 pipeline.AddLast("framing-enc", new LengthFieldPrepender(2));
                                 pipeline.AddLast("framing-dec", new LengthFieldBasedFrameDecoder(ushort.MaxValue, 0, 2, 0, 2));
                                 pipeline.AddLast("echo", new EchoClientHandler());
                             }));
             
            IChannel clientChannel = await bootstrap.ConnectAsync(new IPEndPoint(IPAddress.Parse(hostname),port));
            this.clientChannel = clientChannel;
        }

        void SetupSprot()
        {
            _sprot1 = new cSprot1Parser();
            _sprot1.UseBigEndian = true;
            _sprot1.DestroySelf = this.Dispose;
            _sprot1.packetParser = this.packetParser;
            //_sprot1.MessageQueue = _tcpServerClient.__IncomingDataBuffer;
            _sprot1.MaxPayLoad = 50000000; //this will be an approximate limitation for one command
            _sprot1.DeviceShouldSendAuthorisationBytesBeforeProceedCodec = false;
            _sprot1.ToSendToParserAuthenticationBytes = false;
        }

        internal void FillNodeAddress()
        {
            na = new NodeAddress() { NodeAddressId = Handshake.NodeListeningPort, NodeUId = Handshake.NodeUID, EndPointSID = this.EndPointSID };
        }

        private void packetParser(int codec, byte[] data)
        {

            try
            {
                switch (codec)
                {
                    case 1: //Handshake

                        Handshake = TcpMsgHandshake.BiserDecode(data);
                        if (trn.GetNodeByEntityName("default").NodeAddress.NodeUId != this.Handshake.NodeUID)
                        {
                            //trn.log.Log(new WarningLogEntry()
                            //{
                            //    LogType = WarningLogEntry.eLogType.DEBUG,
                            //    Description = $"{trn.port}> handshake from {this.Handshake.NodeListeningPort}"
                            //});
                        }
                        trn.spider.AddPeerToClusterEndPoints(this, true);
                        return;
                    case 2: //RaftMessage

                        if (this.na == null)
                            return;

                        var msg = TcpMsgRaft.BiserDecode(data);

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

                        Handshake = TcpMsgHandshake.BiserDecode(data);
                        //trn.log.Log(new WarningLogEntry()
                        //{
                        //    LogType = WarningLogEntry.eLogType.DEBUG,
                        //    Description = $"{trn.port}> ACK from {this.Handshake.NodeListeningPort}"
                        //});
                        trn.spider.AddPeerToClusterEndPoints(this, false);

                        return;
                    case 4: //Free Message protocol

                        var Tcpmsg = TcpMsg.BiserDecode(data);
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

                        //if (na != null)
                        //{
                        //    trn.log.Log(new WarningLogEntry()
                        //    {
                        //        LogType = WarningLogEntry.eLogType.DEBUG,
                        //        Description = $"{trn.port} ({trn.rn.NodeState})> peer {na.NodeAddressId} sent ping"
                        //    });
                        //}
                        return;
                }
            }
            catch (Exception ex)
            {
                Dispose();
            }


        }


        object lock_writer = new object();
        bool inWrite = false;
        Queue<byte[]> writerQueue = new Queue<byte[]>();
        Queue<byte[]> highPriorityQueue = new Queue<byte[]>();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="codec"></param>
        /// <param name="data"></param>
        /// <param name="highPriority"></param>
        public void Write(byte[] sprot, bool highPriority = false)
        {
            lock (lock_writer)
            {
                if (highPriority)
                    highPriorityQueue.Enqueue(sprot);
                else
                    writerQueue.Enqueue(sprot);

                if (inWrite)
                    return;

                inWrite = true;
            }

            Task.Run(async () => { await Writer(); });
        }
        public void Send(object msg)
        {
            var text = Newtonsoft.Json.JsonConvert.SerializeObject(msg, new JsonSerializerSettings()
            {
                NullValueHandling = NullValueHandling.Ignore,
                TypeNameHandling = TypeNameHandling.All,
                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Full
            });
            if (this._nettyclient != null)
                this._nettyclient.WriteAndFlushAsync(text);
            else if (this.clientChannel != null)
                this.clientChannel.WriteAndFlushAsync(text);
            else
            {
                Console.WriteLine("null connection");
            }
        }

        /// <summary>
        /// highPriorityQueue is served first
        /// </summary>
        /// <returns></returns>
        async Task Writer()
        {
            if (this.Disposed)
                return;

            byte[] sprot = null;
            try
            {
                while (true)
                {
                    lock (lock_writer)
                    {
                        if (highPriorityQueue.Count == 0 && writerQueue.Count == 0)
                        {
                            inWrite = false;
                            return;
                        }

                        if (highPriorityQueue.Count > 0)
                            sprot = highPriorityQueue.Dequeue();
                        else
                            sprot = writerQueue.Dequeue();
                    }

                    //huge sprot should be splitted, packed into new sprot codec by chunks and supplied here as a standard chunk
                    if (stream != null)
                    {
                        await stream.WriteAsync(sprot, 0, sprot.Length);//.ConfigureAwait(false);
                        await stream.FlushAsync();//.ConfigureAwait(false);
                    }
                    else
                    {
                        await this._nettyclient.WriteAndFlushAsync(sprot);
                    }
                }
            }
            catch (Exception ex)
            {
                Dispose();
            }
        }


        async Task Read()
        {
            try
            {
                //Example of pure tcp
                byte[] rbf = new byte[10000];
                int a = 0;
                if (stream == null)
                {
                    Console.WriteLine("stream null");
                }
                while ((a = await stream.ReadAsync(rbf, 0, rbf.Length)) > 0)
                {
                    _sprot1.MessageQueue.Enqueue(rbf.Substring(0, a));
                    _sprot1.PacketAnalizator(false);
                }

            }
            catch (System.Exception ex)
            {
                //Fires when remote client drops connection //Null reference  
                Console.WriteLine(" error in tcp peer:" + ex.Message);
                Dispose();
            }

        }
        public async Task OnRecieve(IChannelHandlerContext context, object message)
        {
            var buffer = message as IByteBuffer;
            var msgstr = buffer.ToString(Encoding.UTF8);
            //
            var msgObj = Newtonsoft.Json.JsonConvert.DeserializeObject(msgstr);
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

            try
            {
                if (stream != null)
                {
                    stream.Dispose();
                    stream = null;
                }
            }
            catch (Exception)
            { }
            try
            {
                if (_client != null)
                {
                    Console.WriteLine("client dispose");
                    (_client as IDisposable).Dispose();
                    _client = null;
                }
            }
            catch (Exception ex)
            {

            }

            try
            {
                if (_sprot1 != null)
                {
                    _sprot1.MessageQueue.Clear();
                    _sprot1 = null;
                }
            }
            catch (Exception)
            { }

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

        public override void ChannelActive(IChannelHandlerContext context) => context.WriteAndFlushAsync(this.initialMessage);

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            if (message is IByteBuffer byteBuffer)
            {
                Console.WriteLine("Received from server: " + byteBuffer.ToString(Encoding.UTF8));
            }
        }

        public override void ChannelReadComplete(IChannelHandlerContext context) => context.Flush();

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            Console.WriteLine("Exception: " + exception);
            context.CloseAsync();
        }
    }
}//eon
