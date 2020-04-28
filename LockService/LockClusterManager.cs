using Coldairarrow.DotNettyRPC;
using DotNetty.Codecs;
using DotNetty.Codecs.Http;
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using LockServer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Raft;
using Raft.Core.RaftEmulator;
using Raft.Core.Transport;
using Raft.Transport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LockService
{
    public class LockClusterManager
    {
        public static LockClusterManager manager = null;
        public static string PathRoot = @"D:\Temp\RaftDBreeze\node\";
        object sync_nodes = new object();
        public List<LockSeriveControlNode> Nodes { get; set; } = new List<LockSeriveControlNode>();
        public static int CurrentPort = 10000;
        static IWarningLog log = new Logger();
        public static int GetPort()
        {
            return CurrentPort++;
        }
        public async Task StartControlNodes(int num)
        {
            //create 
            LockSeriveControlNode trn = null;
            RaftEntitySettings re_settings = null;
            List<int> ipAddress = new List<int>();
            for (int i = 0; i < num; i++)
            {
                ipAddress.Add(GetPort());
            }
            re_settings = new RaftEntitySettings()
            {
                VerboseRaft = true,
                VerboseTransport = true,
                DelayedPersistenceIsActive = true,
            };
            List<LockSeriveControlNode> nodes = new List<LockSeriveControlNode>();
            for (int i = 0; i < num; i++)
            {
                List<PeerEndPoint> eps = new List<PeerEndPoint>();
                //every node have seperate configuration
                for (int index = 0; index < num; index++)
                    eps.Add(new PeerEndPoint() { Host = "127.0.0.1", Port = ipAddress[index] });
                lock (sync_nodes)
                {
                    int Port = eps[i].Port;
                    var nodeName = "entity" + (i + 1);
                    trn = new LockSeriveControlNode(nodeName,
                        new NodeSettings()
                        {
                            TcpClusterEndPoints = eps,
                            RaftEntitiesSettings = new List<RaftEntitySettings>() { re_settings }
                        },
                        Port
                        , PathRoot + nodeName, log);
                    this.Nodes.Add(trn);

                }
                nodes.Add(trn);
                trn.Start();
                System.Threading.Thread.Sleep((new Random()).Next(30, 350));
            }
            for (int i = 0; i < num; i++)
            {
                await nodes[i].StartConnect();
            }
        }
        public bool isReady()
        {
            var leader = Nodes.Where(r => ((RaftNode)r.InnerNode).IsLeader())
                   .Select(r => (RaftNode)r.InnerNode).FirstOrDefault();

            return (leader != null);
        }
        public bool isWorkerReady()
        {
            var leader = Nodes.Where(r => (r.WorkNode != null) && ((RaftNode)r.WorkNode).IsLeader())
                   .Select(r => ((RaftNode)r.WorkNode)).FirstOrDefault();

            return (leader != null);
        }
        public void TestSendData(ClusterCommand command)
        {
            Task.Run(() =>
            {
                string data = Newtonsoft.Json.JsonConvert.SerializeObject(command);
                //data = "test";
                lock (sync_nodes)
                {
                    if (Nodes.Count < 1)
                        return;
                    var leader = Nodes.Where(r => ((RaftNode)r.InnerNode).IsLeader())
                    .Select(r => (RaftNode)r.InnerNode).FirstOrDefault();
                    if (leader == null)
                        return;
                    ((RaftNode)leader).AddLogEntry(System.Text.Encoding.UTF8.GetBytes(data));
                }
            });
        }
        public void TestSubCluster(ClusterCommand command)
        {
            Task.Run(() =>
            {
                string data = Newtonsoft.Json.JsonConvert.SerializeObject(command);
                //data = "test";
                lock (sync_nodes)
                {
                    if (Nodes.Count < 1)
                        return;
                    var leader = Nodes.Where(r => ((RaftNode)r.InnerNode).IsLeader())
                    .Select(r => (RaftNode)r.InnerNode).FirstOrDefault();
                    if (leader == null)
                        return;
                    ((RaftNode)leader).AddLogEntry(System.Text.Encoding.UTF8.GetBytes(data));
                }
            });
        }
        public async Task<string> TestWorkOperation(LockOper command)
        {
            string data = Newtonsoft.Json.JsonConvert.SerializeObject(command);
            var leader = Nodes.Where(r => ((RaftNode)r.WorkNode).IsLeader())
                     .Select(r => (RaftNode)r.WorkNode).FirstOrDefault();
            if (leader == null) return "null";
            Console.WriteLine("start lock oper" + DateTime.Now.Second + ":" + DateTime.Now.Millisecond);
            var result = await ((RaftNode)leader).AddLogEntryAsync(System.Text.Encoding.UTF8.GetBytes(data));
            Console.WriteLine("await finished" + DateTime.Now.Second + ":" + DateTime.Now.Millisecond);
            return "lockdone";
        }

        public void BootWorkerNode()
        {
            this.TestSendData(
               new ClusterCommand()
               {
                   Command = "CreateShard",
                   Target = "",
                   Targets = new List<string>()
                    {
                          "entity1",
                          "entity2",
                          "entity3"
                    },
                   IpAddress = new List<EndPoint>()
                    {
                          new EndPoint()
                          {
                               ipAddress="127.0.0.1",
                               port=12001
                          },
                           new EndPoint()
                          {
                               ipAddress="127.0.0.1",
                               port=12002
                          },
                            new EndPoint()
                          {
                               ipAddress="127.0.0.1",
                               port=12003
                          }
                    }
               }
               );
        }
        public  void OpenRpc()
        {
            LockClusterManager.manager = this;
            RPCServer rPCServer = new RPCServer(9999);
            rPCServer.RegisterService<IHello, Hello>();
            rPCServer.Start();


        }
        public async void OpenServicePort()
        {
            int port = 9090;
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
                        pipeline.AddLast("encoder", new HttpResponseEncoder());
                        pipeline.AddLast("decoder", new HttpRequestDecoder(4096, 8192, 8192, false));
                        pipeline.AddLast("handler", new HelloServerHandler(this));
                    }));

                // bootstrap绑定到指定端口的行为 就是服务端启动服务，同样的Serverbootstrap可以bind到多个端口
                IChannel boundChannel = await bootstrap.BindAsync(port);
            }
            catch (Exception ex)
            {
                //if (log != null)
                //    log.Log(new WarningLogEntry() { Exception = ex });
            }
        }
    }
    public class LockRequestHandler : ChannelHandlerAdapter //管道处理基类，较常用
    {
        LockClusterManager manager = null;
        public LockRequestHandler(LockClusterManager manager)
        {
            this.manager = manager;
        }


        public override async void ChannelRead(IChannelHandlerContext context, object message)
        {
            LockOper op = new LockOper()
            {
                Key = "key",
                Oper = "lock",
                Session = "session1"
            };
            await manager.TestWorkOperation(op);

        }
        public override void ChannelReadComplete(IChannelHandlerContext context) => context.Flush();

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            Console.WriteLine("Exception: " + exception);
            context.CloseAsync();
        }
    }
    public interface IHello
    {
        string SayHello(string msg);
    }

    public class Hello : IHello
    {
        public  string SayHello(string msg)
        {
            LockOper op = new LockOper()
            {
                Key = "key",
                Oper = "lock",
                Session = "session1"
            };
            var val=Task.Run(async () =>
            {
              return   await LockClusterManager.manager.TestWorkOperation(op).ConfigureAwait(false);
            }).ConfigureAwait(false).GetAwaiter().GetResult();
            return val;
        }
    }
}
