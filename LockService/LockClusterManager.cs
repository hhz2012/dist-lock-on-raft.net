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
        object sync_nodes = new object();
        public List<LockSeriveControlNode> Nodes { get; set; } = new List<LockSeriveControlNode>();
        public static int CurrentPort = 10000;
        static IWarningLog log = new Logger();
        public static int GetPort()
        {
            return CurrentPort++;
        }
        public void StartControlNodes(int num)
        {
            //create 
            LockSeriveControlNode trn = null;
            List<TcpClusterEndPoint> eps = new List<TcpClusterEndPoint>();
            RaftEntitySettings re_settings = null;
            for (int i = 0; i < num; i++)
                eps.Add(new TcpClusterEndPoint() { Host = "127.0.0.1", Port = GetPort() });

            re_settings = new RaftEntitySettings()
            {
                VerboseRaft = true,
                VerboseTransport = false,
                DelayedPersistenceIsActive = true,
            };
            for (int i = 0; i < num; i++)
            {
                lock (sync_nodes)
                {
                    int Port = eps[i].Port;
                    var nodeName = "entity" + (i + 1);
                    trn = new LockSeriveControlNode(nodeName,
                        new NodeSettings() { TcpClusterEndPoints = eps,
                                             RaftEntitiesSettings = new List<RaftEntitySettings>() { re_settings } },
                        Port
                        ,@"D:\Temp\RaftDBreeze\node\" + nodeName, log);
                    this.Nodes.Add(trn);

                }
                trn.Start();
                System.Threading.Thread.Sleep((new Random()).Next(30, 350));
            }
        }
        public void TestSendData(string data)
        {
            Task.Run(() =>
            {
                lock (sync_nodes)
                {
                    if (Nodes.Count < 1)
                        return;
                        var leader = Nodes.Where(r => ((TcpRaftNode)r.InnerNode).IsLeader())
                        .Select(r => (TcpRaftNode)r.InnerNode).FirstOrDefault();

                        if (leader == null)
                            return;

                        ((TcpRaftNode)leader).AddLogEntry(System.Text.Encoding.UTF8.GetBytes(data));
                }
            });
        }
    }
}
