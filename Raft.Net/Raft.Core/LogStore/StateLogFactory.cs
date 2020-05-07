using DBreeze;
using Raft.Core.LogStore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.StateMachine
{
    public class StateLogFactory
    {
        public static IStateLog GetLog(RaftStateMachine node, string workPath)
        {
            return new RocksDBLogStore(node, workPath);
        }
    }
}
