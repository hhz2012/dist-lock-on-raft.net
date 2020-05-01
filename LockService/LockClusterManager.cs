
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
                        new LockClusterManagerHandler(),
                        Port,
                        nodeName,
                        10000+i,
                        new HelloServerHandler(this),
                        log
                        );
                    this.Nodes.Add(trn);
                }
                nodes.Add(trn);
                trn.Start();
                await trn.StartHttp();
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
                var result = await ((RaftServiceNode)leader).AddLogEntryRequestAsync(System.Text.Encoding.UTF8.GetBytes(data)).ConfigureAwait(false);
            }
        );
           // Console.WriteLine("await finished" + DateTime.Now.Second + ":" + DateTime.Now.Millisecond);
            return "lockdone";
        }

    
        
    }
   

}
