using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Raft.Core.Log
{
    public class LeaderSyncState
    {
        public LeaderSyncState(RaftStateMachine rn, string path)
        {
            this.rn = rn;
        }
        internal RaftStateMachine rn = null;
        public ulong tempPrevStateLogId = 0;
        public ulong tempPrevStateLogTerm = 0;
        public ulong tempStateLogId = 0;
        public ulong tempStateLogTerm = 0;


        /// <summary>
        /// Leader only.Stores logs before being distributed.
        /// </summary>       
        public SortedDictionary<ulong, StateLogEntry> qDistribution = new SortedDictionary<ulong, StateLogEntry>();

        /// <summary>
        /// Is called from lock_operations
        /// Adds to silo table, until is moved to log table.
        /// This table can be cleared up on start
        /// returns concatenated term+index inserted identifier
        /// </summary>
        /// <param name="data"></param>
        /// <param name="externalID">if set up must be returned in OnCommitted to notify that command is executed</param>
        /// <returns></returns>
        public StateLogEntry AddStateLogEntryForDistribution(byte[] data, byte[] externalID = null)
        {
            /*
             * Only nodes of the current term can be distributed
             */

            tempPrevStateLogId = tempStateLogId;
            tempPrevStateLogTerm = tempStateLogTerm;

            tempStateLogId++;
            tempStateLogTerm = rn.NodeTerm;


            StateLogEntry le = new StateLogEntry()
            {
                Index = tempStateLogId,
                Data = data,
                Term = tempStateLogTerm,
                PreviousStateLogId = tempPrevStateLogId,
                PreviousStateLogTerm = tempPrevStateLogTerm,
                ExternalID = externalID
            };

            qDistribution.Add(le.Index, le);
            return le;

        }

        /// <summary>
        /// When Node is selected as leader it is cleared
        /// </summary>
        public void ClearLogEntryForDistribution()
        {
            qDistribution.Clear();
        }
        /// <summary>
        /// Returns null if nothing to distribute
        /// </summary>
        /// <returns></returns>
        public StateLogEntrySuggestion GetNextLogEntryToBeDistributed()
        {
            if (qDistribution.Count < 1)
                return null;

            return new StateLogEntrySuggestion()
            {
                StateLogEntry = qDistribution.OrderBy(r => r.Key).First().Value,
                LeaderTerm = rn.NodeTerm
            };
        }
    }
}
