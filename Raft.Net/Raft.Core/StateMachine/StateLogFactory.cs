using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.StateMachine
{
    public class StateLogFactory
    {
        public static IStateLog GetLog(RaftNode node)
        {
            return new StateLog(node);
        }
    }
}
