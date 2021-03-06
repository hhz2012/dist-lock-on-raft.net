﻿using Raft.Core.Handler;
using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.RaftEmulator
{
    public abstract class ActionHandlerBase:IActionHandler
    {
        protected RaftStateMachine raftNode = null;
        public ActionHandlerBase()
        {
            
        }
        public bool SetNode(RaftStateMachine raftNode)
        {
            this.raftNode = raftNode;
            return true;
        }

        abstract public bool DoAction(string entityName, ulong index, byte[] data);
        
    }
}
