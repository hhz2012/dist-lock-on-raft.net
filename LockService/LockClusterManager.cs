using Raft;
using Raft.Core.RaftEmulator;
using Raft.Transport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LockService
{
    public class LockClusterManager
    {
        public static string PathRoot=@"D:\Temp\RaftDBreeze\node\";
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
                        new NodeSettings() { TcpClusterEndPoints = eps,
                                             RaftEntitiesSettings = new List<RaftEntitySettings>() { re_settings } },
                        Port
                        , PathRoot+ nodeName, log);
                    this.Nodes.Add(trn);

                }
                nodes.Add(trn);
                trn.Start();
                System.Threading.Thread.Sleep((new Random()).Next(30, 350));
            }
            for (int i=0;i<num;i++)
            {
                await nodes[i].StartConnect();
            }
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
        public void TestWorkOperation(LockOper command)
        {
            Task.Run(async () =>
            {
                string data = Newtonsoft.Json.JsonConvert.SerializeObject(command);
                //data = "test";
               
                    if (Nodes.Count < 1)
                        return;
                    var leader = Nodes.Where(r => ((RaftNode)r.WorkNode).IsLeader())
                    .Select(r => (RaftNode)r.WorkNode).FirstOrDefault();

                    if (leader == null)
                        return;
                    Console.WriteLine("start lock oper"+DateTime.Now.Second+":"+DateTime.Now.Millisecond);
                    var result=await ((RaftNode)leader).AddLogEntryAsync(System.Text.Encoding.UTF8.GetBytes(data)).ConfigureAwait(false);
                Console.WriteLine("await finished");
               
            });
        }
    }
}
