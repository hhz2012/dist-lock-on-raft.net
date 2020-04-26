using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.StateMachine
{
    public class InMemLog : IStateLog
    {
        public ulong StateLogId { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public ulong StateLogTerm { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public ulong LastCommittedIndex { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public bool LeaderSynchronizationIsActive { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public DateTime LeaderSynchronizationRequestWasSent { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public ulong LastCommittedIndexTerm { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public ulong LastBusinessLogicCommittedIndex { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public uint LeaderSynchronizationTimeOut { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public ulong LastAppliedIndex { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public void AddFakePreviousRecordForInMemoryLatestEntity(ulong prevIndex, ulong prevTerm)
        {
            throw new NotImplementedException();
        }

        public StateLogEntrySuggestion AddNextEntryToStateLogByLeader()
        {
            throw new NotImplementedException();
        }

        public void AddStateLogEntryForDistribution(byte[] data, byte[] externalID = null)
        {
            throw new NotImplementedException();
        }

        public void AddToLogFollower(StateLogEntrySuggestion suggestion)
        {
            throw new NotImplementedException();
        }

        public void BusinessLogicIsApplied(ulong index)
        {
            throw new NotImplementedException();
        }

        public void ClearLogAcceptance()
        {
            throw new NotImplementedException();
        }

        public void ClearLogEntryForDistribution()
        {
            throw new NotImplementedException();
        }

        public void ClearStateLogStartingFromCommitted()
        {
            throw new NotImplementedException();
        }

        public void Clear_dStateLogEntryAcceptance_PeerDisconnected(string endpointsid)
        {
            throw new NotImplementedException();
        }

        public void Debug_PrintOutInMemory()
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public StateLog.eEntryAcceptanceResult EntryIsAccepted(NodeAddress address, uint majorityQuantity, StateLogEntryApplied applied)
        {
            throw new NotImplementedException();
        }

        public void FlushSleCache()
        {
            throw new NotImplementedException();
        }

        public StateLogEntry GetCommitedEntryByIndex(ulong logEntryId)
        {
            throw new NotImplementedException();
        }

        public StateLogEntry GetEntryByIndexTerm(ulong logEntryId, ulong logEntryTerm)
        {
            throw new NotImplementedException();
        }

        public StateLogEntrySuggestion GetNextStateLogEntrySuggestionFromRequested(StateLogEntryRequest req)
        {
            throw new NotImplementedException();
        }

        public bool SetLastCommittedIndexFromLeader(LeaderHeartbeat lhb)
        {
            throw new NotImplementedException();
        }
    }
}
