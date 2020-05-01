using Raft.Transport;
using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.Handler
{
    public interface IBusinessHandler
    {
        bool ExecuteBusinessLogic(StateLogEntry entry,RaftStateMachine node);
    }
}
