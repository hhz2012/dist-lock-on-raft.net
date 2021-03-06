﻿/* 
  Copyright (C) 2018 tiesky.com / Alex Solovyov
  It's a free software for those, who think that it should be free.
*/
using Biser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raft
{
    /// <summary>
    /// Sender of messages, concerning Raft protocol
    /// </summary>
    public interface IPeerConnector
    {

        /// <summary>
        /// 
        /// </summary>
        /// <param name="signalType"></param>
        /// <param name="data"></param>
        /// <param name="senderNodeAddress"></param>
        /// <param name="entityName"></param>
        /// <param name="highPriority"></param>
        void SendToAll(eRaftSignalType signalType, IEncoder data, NodeRaftAddress senderNodeAddress, string entityName, bool highPriority = false);


        /// <summary>
        /// 
        /// </summary>
        /// <param name="nodeAddress"></param>
        /// <param name="signalType"></param>
        /// <param name="data"></param>
        /// <param name="senderNodeAddress"></param>
        /// <param name="entityName"></param>
        void SendTo(NodeRaftAddress nodeAddress,eRaftSignalType signalType, IEncoder data, NodeRaftAddress senderNodeAddress, string entityName);
    }
}
