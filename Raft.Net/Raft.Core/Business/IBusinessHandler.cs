using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.Handler
{
    public interface IBusinessHandler
    {
        bool DoAction(string entityName, ulong index, byte[] data);
        bool SetNode(RaftStateMachine raftNode);

        void BusinessLogicIsApplied(ulong index);
    }
}
