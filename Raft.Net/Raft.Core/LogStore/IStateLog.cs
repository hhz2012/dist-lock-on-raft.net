/* 
  Copyright (C) 2018 tiesky.com / Alex Solovyov
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
//using static Raft.Core.StateMachine.MemStateLog;

namespace Raft
{
    public  interface IStateLog
    {
        public ulong StateLogId { get; set; } 
        public ulong StateLogTerm { get; set; }

        public ulong LastCommittedIndex { get; set; }

        public ulong LastCommittedIndexTerm { get; set; }

        public ulong LastBusinessLogicCommittedIndex { get; set; }

        public ulong LastAppliedIndex { get; set; }
        //leader operation 

        void AddLogEntry(StateLogEntrySuggestion suggestion);
        void AddLogEntryByFollower(StateLogEntrySuggestion suggestion);
        bool CommitLogEntry(NodeRaftAddress address, uint majorityQuantity, StateLogEntryApplied applied);
        SyncResult SyncCommitByHeartBeat(LeaderHeartbeat lhb);

        void RollbackToLastestCommit();
        //common 
        StateLogEntry GetCommitedEntryByIndex(ulong logEntryId);
        StateLogEntry GetEntryByIndexTerm(ulong logEntryId, ulong logEntryTerm);

        //load local log for follower
        StateLogEntrySuggestion GetNextStateLogEntrySuggestion(StateLogEntryRequest req);
        //follower operatoin
    
     
        void AddFakePreviousRecordForInMemoryLatestEntity(ulong prevIndex, ulong prevTerm);
        

        //void Clear_dStateLogEntryAcceptance_PeerDisconnected(string endpointsid);
        void Debug_PrintOutInMemory();
        void Dispose();
       
        
       
    }
    public enum eEntryAcceptanceResult
    {
        NotAccepted,
        Committed,
        AlreadyAccepted,
        Accepted
    }
    class StateLogEntryAcceptance
    {
        public StateLogEntryAcceptance()
        {
            //Quantity = 0;
        }

        ///// <summary>
        ///// Accepted Quantity
        ///// </summary>
        //public uint Quantity { get; set; }
        /// <summary>
        /// StateLogEntry Index
        /// </summary>
        public ulong Index { get; set; }
        /// <summary>
        /// StateLogEntry Term
        /// </summary>
        public ulong Term { get; set; }

        public HashSet<string> acceptedEndPoints = new HashSet<string>();

    }
    public class SyncResult
    {
        public bool Synced { get; set; }
        public bool HasCommit { get; set; }
    }
}