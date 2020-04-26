using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.Handler
{
    public interface IActionHandler
    {
        bool DoAction(string entityName, ulong index, byte[] data);
        bool SetNode(RaftNode raftNode);
    }
}
