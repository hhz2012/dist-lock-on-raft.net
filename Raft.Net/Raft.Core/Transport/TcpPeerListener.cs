using DotNetty.Codecs;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Raft.Transport;
using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.Transport
{
    public class TcpPeerListener
    {
        private int port=0;
        TcpPeerNetwork network = null;
        public TcpPeerListener(TcpPeerNetwork network,int port)
        {
            this.port = port;
            this.network = network;
           
        }
        public void Start()
        {
            DoTcpServer();
        }
        public async void DoTcpServer()
        {
            try
            {
                var bossGroup = new MultithreadEventLoopGroup(1);
                // 工作线程组，默认为内核数*2的线程数
                var workerGroup = new MultithreadEventLoopGroup();

                var bootstrap = new ServerBootstrap();
                bootstrap
                    .Group(bossGroup, workerGroup) // 设置主和工作线程组
                    .Channel<TcpServerSocketChannel>() // 设置通道模式为TcpSocket
                    .Option(ChannelOption.SoBacklog, 100) // 设置网络IO参数等，这里可以设置很多参数，当然你对网络调优和参数设置非常了解的话，你可以设置，或者就用默认参数吧
                                                          //.Handler(new LoggingHandler("SRV-LSTN")) //在主线程组上设置一个打印日志的处理器
                    .ChildHandler(new ActionChannelInitializer<ISocketChannel>(channel =>
                    { //工作线程连接器 是设置了一个管道，服务端主线程所有接收到的信息都会通过这个管道一层层往下传输
                      //同时所有出栈的消息 也要这个管道的所有处理器进行一步步处理
                        IChannelPipeline pipeline = channel.Pipeline;
                        //出栈消息，通过这个handler 在消息顶部加上消息的长度
                        pipeline.AddLast("framing-enc", new LengthFieldPrepender(2));
                        pipeline.AddLast("framing-dec", new LengthFieldBasedFrameDecoder(ushort.MaxValue, 0, 2, 0, 2));
                        //业务handler ，这里是实际处理Echo业务的Handler
                        //pipeline.AddLast("echo", new EchoServerHandler());
                        pipeline.AddLast(new StringEncoder(), new StringDecoder());
                        pipeline.AddLast("echo", new PeerMessageHandler(network));
                    }));

                // bootstrap绑定到指定端口的行为 就是服务端启动服务，同样的Serverbootstrap可以bind到多个端口
                IChannel boundChannel = await bootstrap.BindAsync(this.port);
            }
            catch (Exception ex)
            {
                //if (log != null)
                //    log.Log(new WarningLogEntry() { Exception = ex });
            }
        }
    }
  
}
