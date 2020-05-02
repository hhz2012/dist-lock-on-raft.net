using Raft.Transport;
using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.Handler
{
    public interface IBusinessHandler
    {
        ReturnValueBase ExecuteBusinessLogic(StateLogEntry entry,RaftStateMachine node);
    }
    public class ReturnValueBase
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
