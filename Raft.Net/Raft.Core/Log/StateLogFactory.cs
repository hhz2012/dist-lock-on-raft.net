using DBreeze;
using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.StateMachine
{
    public class StateLogFactory
    {
        public static IStateLog GetLog(RaftStateMachine node, DBreezeEngine dbEngine)
        {
            return new MemStateLog(node);
        }
    }
}
