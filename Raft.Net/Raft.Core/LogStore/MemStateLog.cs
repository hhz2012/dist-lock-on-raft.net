using Raft.Core.Log;
using Raft.Transport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Raft.Core.StateMachine
{
    public class MemStateLog : IStateLog
    {
        /// <summary>
        /// Main table that stores logs
        /// </summary>
        string tblStateLogEntry = "RaftTbl_StateLogEntry";

        ///// <summary>
        ///// Leader only. Stores logs before being distributed.
        ///// </summary>
        //const string tblAppendLogEntry = "RaftTbl_AppendLogEntry";

        internal RaftStateMachine rn = null;
        public ulong LastBusinessLogicCommittedIndex { get; set; } = 0;
        /// <summary>
        /// Holds last committed State Log Index.
        /// If node is leader it sets it, if follower it comes with the Leader's heartbeat
        /// </summary>
        public ulong LastCommittedIndex { get; set; } = 0;
        /// <summary>
        /// Holds last committed State Log Term.
        /// If node is leader it sets it, if follower it comes with the Leader's heartbeat
        /// </summary>
        public ulong LastCommittedIndexTerm { get; set; } = 0;
        /// <summary>
        /// For leaders
        /// Applied entry is a majority of servers can be from previous term. 
        /// It will have the same index as LastCommitted only in case if on majority of servers will be stored at least one entry from current term. Docu.
        /// </summary>
        public ulong LastAppliedIndex { get; set; } = 0;
        /// <summary>
        /// Monotonically grown StateLog Index.
        /// 
        /// </summary>
        public ulong StateLogId { get; set; } = 0;
        public ulong StateLogTerm { get; set; } = 0;

        /// <summary>
        /// Follower part. State Log synchro with Leader.
        /// Indicates that synchronization request was sent to Leader
        /// </summary>
        public bool LeaderSynchronizationIsActive { get; set; } = false;
        /// <summary>
        /// Follower part. State Log synchro with Leader.
        /// Registers DateTime when synchronization request was sent.
        /// Until timeout (LeaderSynchronizationTimeOut normally 1 minute)- no more requests 
        /// </summary>
        public DateTime LeaderSynchronizationRequestWasSent { get; set; } = DateTime.Now;
        /// <summary>
        /// Follower part. State Log synchro with Leader.
        /// </summary>
        public uint LeaderSynchronizationTimeOut { get; set; } = 1;
        /// <summary>
        /// Only for the Leader.
        /// Key is StateLogEntryId, Value contains information about how many nodes accepted LogEntry
        /// </summary>
        Dictionary<ulong, StateLogEntryAcceptance> dStateLogEntryAcceptance = new Dictionary<ulong, StateLogEntryAcceptance>();

        IndexTermDict<StateLogEntry> inMem = new IndexTermDict<StateLogEntry>();
        LeaderSyncState leaderState = null;
        public MemStateLog(RaftStateMachine rn,string path)
        {
            this.rn = rn;
            tblStateLogEntry = "mem_" + tblStateLogEntry;
            leaderState = new LeaderSyncState(rn,path);
        }
        /// <summary>
        /// Secured by RaftNode
        /// </summary>
        public void Dispose()
        {

        }
        /// <summary>
        /// under lock_operations
        /// Copyies from distribution silo table and puts in StateLog table       
        /// </summary>
        /// <returns></returns>
        public StateLogEntrySuggestion AddNextEntryToStateLogByLeader()
        {
            var suggest = this.leaderState.GetNextLogEntryToBeDistributed();
            if (suggest == null)
                return null;

            //Restoring current values
            //PreviousStateLogId = suggest.StateLogEntry.PreviousStateLogId;
            //PreviousStateLogTerm = suggest.StateLogEntry.PreviousStateLogTerm;
            StateLogId = suggest.StateLogEntry.Index;
            StateLogTerm = suggest.StateLogEntry.Term;
            lock (inMem.Sync)
            {
                inMem.Add(suggest.StateLogEntry.Index, suggest.StateLogEntry.Term, suggest.StateLogEntry);
            }
            return suggest;
        }

        /// <summary>
        /// under lock_operations
        /// only for followers
        /// </summary>
        /// <param name="lhb"></param>
        /// <returns>will return false if node needs synchronization from LastCommittedIndex/Term</returns>
        public bool SetLastCommittedIndexFromLeader(LeaderHeartbeat lhb)
        {
            if (this.LastCommittedIndex < lhb.LastStateLogCommittedIndex)
            {
                //Node tries to understand if it contains already this index/term, if not it will need synchronization

                ulong populateFrom = 0;
                lock (inMem.Sync)
                {
                    if (inMem.Select(lhb.LastStateLogCommittedIndex, lhb.LastStateLogCommittedIndexTerm, out var rSle))
                    {
                        populateFrom = this.LastCommittedIndex + 1;

                        this.LastCommittedIndex = lhb.LastStateLogCommittedIndex;
                        this.LastCommittedIndexTerm = lhb.LastStateLogCommittedIndexTerm;
                    }
                    else
                        return false;
                }
                if (populateFrom > 0)
                    this.rn.logHandler.Commited();
            }
            return true;
        }
        /// <summary>
        /// +
        /// Only Follower makes it. Clears its current Log
        /// Clearing 
        /// </summary>
        /// <param name="logEntryId"></param>       
        public void ClearStateLogStartingFromCommitted()
        {
            if (LastCommittedIndex == 0 || LastCommittedIndexTerm == 0)
                return;
            lock (inMem.Sync)
            {
                inMem.Remove(
                    inMem.SelectForwardFromTo(LastCommittedIndex, LastCommittedIndexTerm, false, ulong.MaxValue, ulong.MaxValue).ToList()
                    );
            }
        }
    
        /// <summary>
        /// under lock_operation control
        /// </summary>
        /// <param name="index"></param>
        public void BusinessLogicIsApplied(ulong index)
        {
            LastBusinessLogicCommittedIndex = index;
            lock (inMem.Sync)
            {
                var removeFromIndex = inMem.GetOneIndexDownFrom(index);
                if (removeFromIndex > 0)
                    inMem.Remove(inMem.SelectBackwardFromTo(removeFromIndex - 1, ulong.MaxValue, true, 0, 0).ToList());
            }
        }

        /// <summary>
        /// +
        /// Can be null.
        /// Must be called inside of operation lock.
        /// </summary>
        /// <param name="logEntryId"></param>
        /// <param name="LeaderTerm"></param>
        /// <returns></returns>
        public StateLogEntrySuggestion GetNextStateLogEntrySuggestionFromRequested(StateLogEntryRequest req)
        {
            StateLogEntrySuggestion le = new StateLogEntrySuggestion()
            {
                LeaderTerm = rn.NodeTerm
            };
            return le;
        }
        /// <summary>
        /// +
        /// Get Term by EntryLogIndex. Returns First element false if not found, Second - Term (if found).
        /// Must be called inside of operation lock.
        /// </summary>
        /// <param name="logId"></param>
        /// <returns>first element true if exists</returns>
        public StateLogEntry GetEntryByIndexTerm(ulong logEntryId, ulong logEntryTerm)
        {
            try
            {
                Tuple<ulong, StateLogEntry> sleTpl;
                StateLogEntry isle = null;
                lock (inMem.Sync)
                {
                    inMem.Select(logEntryId, logEntryTerm, out isle);
                }
                return isle;

            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// +
        /// Must be called inside of operation lock.
        /// </summary>
        /// <param name="logEntryId"></param>
        /// <returns></returns>
        public StateLogEntry GetCommitedEntryByIndex(ulong logEntryId)
        {
            try
            {
                if (this.LastCommittedIndex < logEntryId)
                    return null;

                StateLogEntry isle = null;
                lock (inMem.Sync)
                {
                    foreach (var el in inMem.SelectForwardFromTo(logEntryId, ulong.MinValue, true, ulong.MaxValue, ulong.MaxValue))
                    {
                        if (el.Item3.FakeEntry)
                            continue;
                        return el.Item3;
                    }

                    return isle;
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="suggestion"></param>
        /// <returns></returns>
        public void AddToLogFollower(StateLogEntrySuggestion suggestion)
        {
            try
            {
                int pups = 0;
                lock (inMem.Sync)
                {
                    List<Tuple<ulong, ulong, StateLogEntry>> toDelete = new List<Tuple<ulong, ulong, StateLogEntry>>();

                    foreach (var el in inMem.SelectForwardFromTo(suggestion.StateLogEntry.Index, ulong.MinValue, true, ulong.MaxValue, ulong.MaxValue))
                    {
                        if (el.Item1 == suggestion.StateLogEntry.Index && el.Item2 == suggestion.StateLogEntry.Term)
                        {
                            if (pups > 0)
                            {
                                //!!!!!!!!!!!!!!! note, to be checked, it returns, but may be it could have smth to remove before (earlier term - ulong.MinValue is used )
                                throw new Exception("Pups more than 0");
                            }
                            return;
                        }
                        toDelete.Add(el);
                        pups++;
                    }
                    if (toDelete.Count > 0)
                        inMem.Remove(toDelete);
                    inMem.Add(suggestion.StateLogEntry.Index, suggestion.StateLogEntry.Term, suggestion.StateLogEntry);
                }

             //   rn.VerbosePrint($"{rn.NodeAddress.NodeAddressId}> AddToLogFollower (I/T): {suggestion.StateLogEntry.Index}/{suggestion.StateLogEntry.Term} -> Result:" +
              //      $" { (GetEntryByIndexTerm(suggestion.StateLogEntry.Index, suggestion.StateLogEntry.Term) != null)};");

                //Setting new internal LogId
                this.leaderState.tempPrevStateLogId = StateLogId;
                this.leaderState.tempPrevStateLogTerm = StateLogTerm;
                StateLogId = suggestion.StateLogEntry.Index;
                StateLogTerm = suggestion.StateLogEntry.Term;

                //tempPrevStateLogId = PreviousStateLogId;
                //tempPrevStateLogTerm = PreviousStateLogTerm;
                this.leaderState.tempStateLogId = StateLogId;
                this.leaderState.tempStateLogTerm = StateLogTerm;

                if (suggestion.IsCommitted)
                {
                    if (
                        this.LastCommittedIndexTerm > suggestion.StateLogEntry.Term
                        ||
                        (
                            this.LastCommittedIndexTerm == suggestion.StateLogEntry.Term
                            &&
                            this.LastCommittedIndex > suggestion.StateLogEntry.Index
                         )
                    )
                    {
                        //Should be not possible
                    }
                    else
                    {
                        this.LastCommittedIndex = suggestion.StateLogEntry.Index;
                        this.LastCommittedIndexTerm = suggestion.StateLogEntry.Term;

                        //this.rn.Commited(suggestion.StateLogEntry.Index);                      
                        this.rn.logHandler.Commited();
                    }
                }
            }
            catch (Exception ex)
            {

            }
            //if (this.LastCommittedIndex < rn.LeaderHeartbeat.LastStateLogCommittedIndex)
            if (rn.LeaderHeartbeat != null && this.LastCommittedIndex < rn.LeaderHeartbeat.LastStateLogCommittedIndex)
            {
                rn.SyncronizeWithLeader(true);
            }
            else
                LeaderSynchronizationIsActive = false;
        }
        public void Clear_dStateLogEntryAcceptance_PeerDisconnected(string endpointsid)
        {
            foreach (var el in dStateLogEntryAcceptance)
                el.Value.acceptedEndPoints.Remove(endpointsid);
        }
        /// <summary>
        /// +
        /// Only Leader's proc.
        /// Accepts entry return true if Committed
        /// </summary>
        /// <param name="majorityNumber"></param>
        /// <param name="LogId"></param>
        /// <param name="TermId"></param>        
        public eEntryAcceptanceResult EntryIsAccepted(NodeRaftAddress address, uint majorityQuantity, StateLogEntryApplied applied)
        {
            //If we receive acceptance signals of already Committed entries, we just ignore them
            if (applied.StateLogEntryId <= this.LastCommittedIndex)
                return eEntryAcceptanceResult.AlreadyAccepted;    //already accepted
            if (applied.StateLogEntryId <= this.LastAppliedIndex)
                return eEntryAcceptanceResult.AlreadyAccepted;    //already accepted

            StateLogEntryAcceptance acc = null;
            if (dStateLogEntryAcceptance.TryGetValue(applied.StateLogEntryId, out acc))
            {
                if (acc.Term != applied.StateLogEntryTerm)
                    return eEntryAcceptanceResult.NotAccepted;   //Came from wrong Leader probably
                acc.acceptedEndPoints.Add(address.EndPointSID);
            }
            else
            {
                acc = new StateLogEntryAcceptance()
                {
                    //Quantity = 2,  //Leader + first incoming
                    Index = applied.StateLogEntryId,
                    Term = applied.StateLogEntryTerm
                };
                acc.acceptedEndPoints.Add(address.EndPointSID);
                dStateLogEntryAcceptance[applied.StateLogEntryId] = acc;
            }
            if ((acc.acceptedEndPoints.Count + 1) >= majorityQuantity)
            {
                this.LastAppliedIndex = applied.StateLogEntryId;
                //Removing from Dictionary
                dStateLogEntryAcceptance.Remove(applied.StateLogEntryId);

                if (this.LastCommittedIndex < applied.StateLogEntryId && rn.NodeTerm == applied.StateLogEntryTerm)    //Setting LastCommittedId
                {
                    //Saving committed entry (all previous are automatically committed)
                    List<byte[]> lstCommited = new List<byte[]>();
                    lock (inMem.Sync)
                    {
                        foreach (var el in inMem.SelectForwardFromTo(this.LastCommittedIndex + 1, applied.StateLogEntryTerm, true, ulong.MaxValue, applied.StateLogEntryTerm))
                        {
                            lstCommited.Add(el.Item3.Data);
                        }
                    }
                    //Removing entry from command queue
                    //t.RemoveKey<byte[]>(tblAppendLogEntry, new byte[] { 1 }.ToBytes(applied.StateLogEntryTerm, applied.StateLogEntryId));                        
                    this.leaderState.qDistribution.Remove(applied.StateLogEntryId);
                }
                this.LastCommittedIndex = applied.StateLogEntryId;
                this.LastCommittedIndexTerm = applied.StateLogEntryTerm;
                this.rn.logHandler.Commited();
                return eEntryAcceptanceResult.Committed;
            }

            return eEntryAcceptanceResult.Accepted;
        }

        /// <summary>
        /// +
        /// When node becomes a Leader, it clears acceptance log
        /// </summary>
        public void ClearLogAcceptance()
        {
            this.dStateLogEntryAcceptance.Clear();
        }

        public void AddFakePreviousRecordForInMemoryLatestEntity(ulong prevIndex, ulong prevTerm)
        {
            lock (inMem.Sync)
            {
                inMem.Add(prevIndex, prevTerm, new StateLogEntry { Data = null, FakeEntry = true, Index = prevIndex, Term = prevTerm });
            }
        }

        public void Debug_PrintOutInMemory()
        {
            if (rn.entitySettings.InMemoryEntity)
            {
                lock (inMem.Sync)
                {
                    foreach (var el in inMem.SelectForwardFromTo(ulong.MinValue, ulong.MinValue, true, ulong.MaxValue, ulong.MaxValue))
                    {
                        Console.WriteLine($"I: {el.Item1}; T: {el.Item2}; S: {el.Item3.IsCommitted}");
                    }
                }
            }
            else
            {
                Console.WriteLine("Debug_PrintOutInMemory failed - not InMemory entity");
            }
        }

        public StateLogEntry AddStateLogEntryForDistribution(byte[] data, byte[] externalID = null)
        {
            return this.leaderState.AddStateLogEntryForDistribution(data, externalID);
        }

        public void ClearLogEntryForDistribution()
        {
            this.leaderState.ClearLogEntryForDistribution();
        }
    }
}
