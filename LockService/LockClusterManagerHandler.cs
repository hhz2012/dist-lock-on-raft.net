using LockQueueLib;
using LockService;
using Raft.Core.Handler;

using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core
{
    public class LockClusterManagerHandler : IBusinessHandler
    {
        LockTable table = new LockTable();
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

        public ReturnValueBase ExecuteBusinessLogic(StateLogEntry entry, RaftStateMachine node)
        {
            node.NodeStateLog.LastBusinessLogicCommittedIndex = entry.Index;
            if (GlobalConfig.Verbose)
            {
                Console.WriteLine("receive entry:"+entry.Index+" on node:"+node.NodeName);
            }
            string text=System.Text.Encoding.UTF8.GetString(entry.Data);
            var cmd=Newtonsoft.Json.JsonConvert.DeserializeObject<LockOper>(text);
            if (cmd!=null)
            {
                bool succ=this.table.GetQueue(cmd.Key).LockNoWait(cmd.Session,cmd.Type);
                return new ReturnValueBase
                {
                    Success = succ,
                    Message= "operation finished"
                };
            }
            return new ReturnValueBase()
            {
                Success = false,
                Message = "wrong command"
            };
            
        }
    }
}
