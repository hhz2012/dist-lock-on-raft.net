/* 
  Copyright (C) 2018 tiesky.com / Alex Solovyov
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raft
{
    /// <summary>
    /// Receiver of incoming messages concerning RAFT protocol
    /// </summary>
    public interface IRaftStateMachine
    {
        
        /// <param name="address">Address of the node-sender</param>
        /// <param name="signalType"></param>
        /// <param name="data"></param>
        void HandleRaftSignal(NodeRaftAddress address, eRaftSignalType signalType, object data);
    }
}
