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
using Raft.Core.Raft;
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
        public List<HttpRaftServiceNode> Nodes { get; set; } = new List<HttpRaftServiceNode>();
        public static int CurrentPort = 10000;
        static IWarningLog log = new Logger();
        public static int GetPort()
        {
            return CurrentPort++;
        }
        public async Task StartControlNodes(int num)
        {
            //create 
            HttpRaftServiceNode trn = null;
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
            List<HttpRaftServiceNode> nodes = new List<HttpRaftServiceNode>();
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
                    trn = new HttpRaftServiceNode(
                        new NodeSettings()
                        {
                            TcpClusterEndPoints = eps,
                            RaftEntitiesSettings =  re_settings 
                        },
                        PathRoot + nodeName,
                        null,
                        Port,
                        nodeName,
                        10000,
                        null,
                        log
                        );
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
            var leader = Nodes.Where(r => ((HttpRaftServiceNode)r).IsLeader())
                   .Select(r => (HttpRaftServiceNode)r).FirstOrDefault();

            return (leader != null);
        }
        public bool isWorkerReady()
        {
            var leader = Nodes.Where(r => (r!= null) && ((HttpRaftServiceNode)r).IsLeader())
                   .Select(r => ((HttpRaftServiceNode)r)).FirstOrDefault();

            return (leader != null);
        }
       
     
        public async Task<string> TestWorkOperation(LockOper command)
        {
            string data = Newtonsoft.Json.JsonConvert.SerializeObject(command);
            var leader = Nodes.Where(r => ((HttpRaftServiceNode)r).IsLeader())
                     .Select(r => (HttpRaftServiceNode)r).FirstOrDefault();
            if (leader == null) return "null";
            //Console.WriteLine("start lock oper" + DateTime.Now.Second + ":" + DateTime.Now.Millisecond);
            await Task.Run(async () =>
            {
                var result = await ((RaftServiceNode)leader).AddLogEntryAsync(System.Text.Encoding.UTF8.GetBytes(data)).ConfigureAwait(false);
            }
        );
           // Console.WriteLine("await finished" + DateTime.Now.Second + ":" + DateTime.Now.Millisecond);
            return "lockdone";
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
            var val = "val";
            //var val=Task.Run(async () =>
            //{
            //  return   await LockClusterManager.manager.TestWorkOperation(op).ConfigureAwait(false);
            //}).ConfigureAwait(false).GetAwaiter().GetResult();
            return val;
        }
    }
}
