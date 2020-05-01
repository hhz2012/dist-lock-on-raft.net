/* 
  Copyright (C) 2018 tiesky.com / Alex Solovyov
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using static Raft.Core.StateMachine.MemStateLog;

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
             
        eEntryAcceptanceResult EntryIsAccepted(NodeRaftAddress address, uint majorityQuantity, StateLogEntryApplied applied);
        //load local log for follower
        StateLogEntrySuggestion GetNextStateLogEntrySuggestionFromRequested(StateLogEntryRequest req);
        //follower operatoin
        void AddToLogFollower(StateLogEntrySuggestion suggestion);
        //common 
        StateLogEntry GetCommitedEntryByIndex(ulong logEntryId);
        StateLogEntry GetEntryByIndexTerm(ulong logEntryId, ulong logEntryTerm);
        void AddFakePreviousRecordForInMemoryLatestEntity(ulong prevIndex, ulong prevTerm);
        void BusinessLogicIsApplied(ulong index);

        //clear operations
        void ClearLogAcceptance();
   
        void ClearStateLogStartingFromCommitted();
        void Clear_dStateLogEntryAcceptance_PeerDisconnected(string endpointsid);
        void Debug_PrintOutInMemory();
        void Dispose();
       
        
        bool SetLastCommittedIndexFromLeader(LeaderHeartbeat lhb);
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
}