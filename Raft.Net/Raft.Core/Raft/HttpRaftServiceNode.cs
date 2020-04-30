using DotNetty.Buffers;
using DotNetty.Codecs.Http;
using DotNetty.Common;
using DotNetty.Common.Utilities;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Raft.Core.Handler;
using Raft.Transport;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Raft.Core.Raft
{
    public class HttpRaftServiceNode : RaftServiceNode
    {
        int httpPort=0;
        ChannelHandlerAdapter adapter = null;

        public HttpRaftServiceNode(NodeSettings nodeSettings,
                                   string dbreezePath,
                                   IActionHandler handler,
                                   int port = 4250,
                                   string nodeName = "default",
                                   int httpPort=10000,
                                   ChannelHandlerAdapter adapter=null,
                                   IWarningLog log = null)
            :base(nodeSettings,dbreezePath,handler,port,nodeName,log)
        {
            this.httpPort = httpPort;
            this.adapter = adapter;
        }
      
      
        //static 
        public async Task StartHttp()
        {
            ServerBootstrap bootstrap = null;
            try
            {
               // if (bootstrap == null)
                {
                    IEventLoopGroup group;
                    IEventLoopGroup workGroup;
                    bootstrap = new ServerBootstrap();
                    group = new MultithreadEventLoopGroup(1);
                    workGroup = new MultithreadEventLoopGroup();
                    
                    bootstrap.Group(group, workGroup);
                    bootstrap.Channel<TcpServerSocketChannel>();
                    bootstrap
                        .Option(ChannelOption.SoBacklog, 8192)
                        .ChildHandler(new ActionChannelInitializer<IChannel>(channel =>
                        {
                            IChannelPipeline pipeline = channel.Pipeline;
                        //if (tlsCertificate != null)
                        //{
                        //    pipeline.AddLast(TlsHandler.Server(tlsCertificate));
                        //}
                            pipeline.AddLast("encoder", new HttpResponseEncoder());
                            pipeline.AddLast("decoder", new HttpRequestDecoder(4096, 8192, 8192, false));
                            pipeline.AddLast("handler", this.adapter);
                        }));
                    IChannel bootstrapChannel = await bootstrap.BindAsync(System.Net.IPAddress.IPv6Any, this.port);
                }
                if (bootstrap == null)
                {
                    
                }
            }
            catch (Exception ex)
            {
                //if (log != null)
                //    log.Log(new WarningLogEntry() { Exception = ex });
            }
        }
    }
   
  
}
