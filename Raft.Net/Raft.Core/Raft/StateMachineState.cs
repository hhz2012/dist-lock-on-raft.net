using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.Raft
{
    public class StateMachineState
    {
        /// <summary>
        /// Received Current leader heartbeat time
        /// </summary>
        public DateTime LeaderHeartbeatArrivalTime = DateTime.MinValue;
        public uint LeaderSynchronizationTimeOut { get; set; }
        /// <summary>
        /// Latest heartbeat from leader, can be null on start
        /// </summary>
        /// 
        internal LeaderHeartbeat LeaderHeartbeat = null;
        public HashSet<string> VotesQuantity = new HashSet<string>();
        //state 
        public eNodeState NodeState = eNodeState.Follower;
        /// <summary>
        /// Is node started 
        /// </summary>
        public bool IsRunning = false;

        

        public int inCommit = 0;
        public bool InLogEntrySend = false;
        public bool LeaderSynchronizationIsActive { get; set; }

        public DateTime LeaderSynchronizationRequestWasSent { get; set; }
    }
}
