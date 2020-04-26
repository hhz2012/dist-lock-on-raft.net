using Raft;
using Raft.Core.RaftEmulator;
using Raft.Transport;
using System;
using System.Collections.Generic;
using System.Text;

namespace LockService
{
    public class LockSeriveControlNode
    {
        TcpRaftNode trn = null;
        public LockSeriveControlNode(string nodeName, NodeSettings setting,int Port,string localPath,IWarningLog logger)
        {
            
            trn = new TcpRaftNode(setting,
                                 localPath,              
                                 new ClusterHandler(),
                                 Port,
                                 nodeName+"_Control",
                                 logger);
        }
        public void Start()
        {
            trn.Start();
        }
        public TcpRaftNode InnerNode
        {
            get
            {
                return this.trn;
            }
        }
    }
}
