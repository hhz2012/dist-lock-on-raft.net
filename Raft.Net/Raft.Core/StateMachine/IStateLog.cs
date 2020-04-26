/* 
  Copyright (C) 2018 tiesky.com / Alex Solovyov
  It's a free software for those, who think that it should be free.
*/
using System;

namespace Raft
{
    public  interface IStateLog
    {
        public ulong StateLogId { get; set; } 
        public ulong StateLogTerm { get; set; }

        public ulong LastCommittedIndex { get; set; }

        public bool LeaderSynchronizationIsActive { get; set; }
    
        public DateTime LeaderSynchronizationRequestWasSent { get; set; }

        public ulong LastCommittedIndexTerm { get; set; }

        public ulong LastBusinessLogicCommittedIndex { get; set; }

        public uint LeaderSynchronizationTimeOut { get; set; }

        public ulong LastAppliedIndex { get; set; }

        void AddFakePreviousRecordForInMemoryLatestEntity(ulong prevIndex, ulong prevTerm);
        StateLogEntrySuggestion AddNextEntryToStateLogByLeader();
        void AddStateLogEntryForDistribution(byte[] data, byte[] externalID = null);
        void AddToLogFollower(StateLogEntrySuggestion suggestion);
        void BusinessLogicIsApplied(ulong index);
        void ClearLogAcceptance();
        void ClearLogEntryForDistribution();
        void ClearStateLogStartingFromCommitted();
        void Clear_dStateLogEntryAcceptance_PeerDisconnected(string endpointsid);
        void Debug_PrintOutInMemory();
        void Dispose();
        StateLog.eEntryAcceptanceResult EntryIsAccepted(NodeAddress address, uint majorityQuantity, StateLogEntryApplied applied);
        void FlushSleCache();
        StateLogEntry GetCommitedEntryByIndex(ulong logEntryId);
        StateLogEntry GetEntryByIndexTerm(ulong logEntryId, ulong logEntryTerm);
        StateLogEntrySuggestion GetNextStateLogEntrySuggestionFromRequested(StateLogEntryRequest req);
        bool SetLastCommittedIndexFromLeader(LeaderHeartbeat lhb);
    }
}