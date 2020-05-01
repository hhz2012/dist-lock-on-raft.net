using LockService;
using Raft.Core.Handler;

using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.RaftEmulator
{
    public class LockClusterManagerHandler : IBusinessHandler
    {
        
        public LockClusterManagerHandler( )
        {
            
        }

        public void ExecuteBusinessLogic(ulong index)
        {
            throw new NotImplementedException();
        }

        public bool DoAction(string entityName, ulong index, byte[] data)
        {
            throw new NotImplementedException();
        }

        public bool SetNode(RaftStateMachine raftNode)
        {
            return true;
        }

        public bool ExecuteBusinessLogic(StateLogEntry entry, RaftStateMachine node)
        {
            return true;
        }
    }
}
