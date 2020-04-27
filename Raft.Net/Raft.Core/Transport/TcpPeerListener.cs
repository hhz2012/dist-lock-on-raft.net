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
                        pipeline.AddLast("echo", new EchoServerHandler(network));
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
    public class EchoServerHandler : ChannelHandlerAdapter //管道处理基类，较常用
    {
        TcpPeerNetwork connector = null;
        public EchoServerHandler(TcpPeerNetwork connector)
        {
            this.connector = connector;
        }
        TcpPeer peer = null;
        //	重写基类的方法，当消息到达时触发，这里收到消息后，在控制台输出收到的内容，并原样返回了客户端
        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            var str = message as string;
            if (peer == null)
            {
                peer = connector.AddTcpClient(context);
                peer.OnRecieve(context, str).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            else
            {
                peer.OnRecieve(context, str).ConfigureAwait(false).GetAwaiter().GetResult();
            }

        }

        // 输出到客户端，也可以在上面的方法中直接调用WriteAndFlushAsync方法直接输出
        public override void ChannelReadComplete(IChannelHandlerContext context) => context.Flush();

        //捕获 异常，并输出到控制台后断开链接，提示：客户端意外断开链接，也会触发
        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            Console.WriteLine("Exception: " + exception);
            context.CloseAsync();
        }
    }
}
