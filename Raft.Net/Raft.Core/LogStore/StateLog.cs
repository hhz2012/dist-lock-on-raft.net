/* 
  Copyright (C) 2018 tiesky.com / Alex Solovyov
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using DBreeze;
using DBreeze.Utils;

namespace Raft
{

    public class StateLog : IDisposable, IStateLog
    {
        /// <summary>
        /// Main table that stores logs
        /// </summary>
        string stateTableName = "RaftTbl_StateLogEntry";

        ///// <summary>
        ///// Leader only. Stores logs before being distributed.
        ///// </summary>
        internal RaftStateMachine statemachine = null;
        /// <summary>
        /// Holds last committed State Log Index.
        /// If node is leader it sets it, if follower it comes with the Leader's heartbeat
        /// </summary>
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

        public ulong PreviousStateLogId = 0;
        public ulong PreviousStateLogTerm = 0;

        ///// <summary>
        ///// Follower part. State Log synchro with Leader.
        ///// Indicates that synchronization request was sent to Leader
        ///// </summary>
        //public bool LeaderSynchronizationIsActive { get; set; } = false;
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


        SortedDictionary<ulong, Tuple<ulong, StateLogEntry>> sleCache = new SortedDictionary<ulong, Tuple<ulong, StateLogEntry>>();
        ulong sleCacheIndex = 0;
        ulong sleCacheTerm = 0;
        ulong sleCacheBusinessLogicIndex = 0;




        DBreezeEngine db = null;
        public StateLog(RaftStateMachine rn, DBreezeEngine dbEngine)
        {
            this.statemachine = rn;
            this.db = dbEngine;

            if (rn.entitySettings.EntityName != "default")
                stateTableName += "_" + rn.entitySettings.EntityName;

            using (var t = this.db.GetTransaction())
            {
                var row = t.SelectBackwardFromTo<byte[], byte[]>(stateTableName,
                    new byte[] { 1 }.ToBytes(ulong.MaxValue, ulong.MaxValue), true,
                    new byte[] { 1 }.ToBytes(ulong.MinValue, ulong.MinValue), true)
                    .FirstOrDefault();
                StateLogEntry sle = null;
                if (row != null && row.Exists)
                {
                    sle = StateLogEntry.BiserDecode(row.Value);
                    StateLogId = sle.Index;
                    StateLogTerm = sle.Term;
                    PreviousStateLogId = sle.PreviousStateLogId;
                    PreviousStateLogTerm = sle.PreviousStateLogTerm;

                    //tempPrevStateLogId = PreviousStateLogId;
                    //tempPrevStateLogTerm = PreviousStateLogTerm;
                    //tempStateLogId = StateLogId;
                    //tempStateLogTerm = StateLogTerm;
                    rn.NodeTerm = sle.Term;
                }
                var rowTerm = t.Select<byte[], byte[]>(stateTableName, new byte[] { 2 });
                if (rowTerm.Exists)
                {
                    LastCommittedIndex = rowTerm.Value.Substring(0, 8).To_UInt64_BigEndian();
                    LastCommittedIndexTerm = rowTerm.Value.Substring(8, 8).To_UInt64_BigEndian();
                }

                var rowBL = t.Select<byte[], ulong>(stateTableName, new byte[] { 3 });
                if (rowBL.Exists)
                {
                    LastBusinessLogicCommittedIndex = rowBL.Value;
                }
            }
        }

        /// <summary>
        /// Secured by RaftNode
        /// </summary>
        public void Dispose()
        {
        }






      
        /// <summary>
        /// under lock_operations
        /// only for followers
        /// </summary>
        /// <param name="lhb"></param>
        /// <returns>will return false if node needs synchronization from LastCommittedIndex/Term</returns>
        public SyncResult SyncCommitByHeartBeat(LeaderHeartbeat lhb)
        {
            if (this.LastCommittedIndex < lhb.LastStateLogCommittedIndex)
            {
                //Node tries to understand if it contains already this index/term, if not it will need synchronization
                ulong populateFrom = 0;
                using (var t = this.db.GetTransaction())
                {
                    t.ValuesLazyLoadingIsOn = false;
                    //find leader last commit record  in database
                    var row = t.Select<byte[], byte[]>(stateTableName, (new byte[] { 1 }).ToBytes(lhb.LastStateLogCommittedIndex, lhb.LastStateLogCommittedIndexTerm));
                    if (row.Exists)
                    {
                        populateFrom = this.LastCommittedIndex + 1;
                        this.LastCommittedIndex = lhb.LastStateLogCommittedIndex;
                        this.LastCommittedIndexTerm = lhb.LastStateLogCommittedIndexTerm;
                        t.Insert<byte[], byte[]>(stateTableName, new byte[] { 2 }, lhb.LastStateLogCommittedIndex.ToBytes(lhb.LastStateLogCommittedIndexTerm));
                        t.Commit();
                    }
                    else
                        return new SyncResult()
                        {
                             Synced=false,
                             HasCommit=false,
                        };
                }
                if (populateFrom > 0)
                    return new SyncResult()
                    {
                        Synced = true,
                        HasCommit = true,
                    };
            }
            return new SyncResult()
            {
                Synced = true,
                HasCommit = false,
            }; 
        }
        /// <summary>
        /// +
        /// Only Follower makes it. Clears its current Log
        /// Clearing 
        /// sync with leader, remove all log leader don't have 
        /// </summary>
        /// <param name="logEntryId"></param>       
        public void RollbackToLastestCommit()
        {
            if (LastCommittedIndex == 0 || LastCommittedIndexTerm == 0)
                return;

            //FlushSleCache();
            using (var t = this.db.GetTransaction())
            {
                //Removing from the persisted all keys equal or bigger then suppled Log
                foreach (var el in t.SelectForwardFromTo<byte[], byte[]>(stateTableName,
                            new byte[] { 1 }.ToBytes(LastCommittedIndex, LastCommittedIndexTerm), false,
                            new byte[] { 1 }.ToBytes(ulong.MaxValue, ulong.MaxValue), true, true))
                {
                    t.RemoveKey<byte[]>(stateTableName, el.Key);
                }
                t.Commit();
            }
        }

        /// <summary>
        /// update all cache entry into database
        /// </summary>
        public void FlushSleCache()
        {
            if (!statemachine.entitySettings.DelayedPersistenceIsActive)
                return;
            if (sleCache.Count > 0 || sleCacheIndex > 0 || sleCacheTerm > 0 || sleCacheBusinessLogicIndex > 0)
            {
                statemachine.VerbosePrint($"{statemachine.NodeAddress.NodeAddressId}> flushing: {sleCache.Count}");

                using (var t = this.db.GetTransaction())
                {
                    if (sleCache.Count > 0)
                    {
                        foreach (var el in sleCache)
                            t.Insert<byte[], byte[]>(stateTableName, new byte[] { 1 }.ToBytes(el.Key, el.Value.Item1), el.Value.Item2.SerializeBiser());

                        sleCache.Clear();
                    }

                    if (sleCacheIndex > 0 && sleCacheTerm > 0)
                        t.Insert<byte[], byte[]>(stateTableName, new byte[] { 2 }, sleCacheIndex.ToBytes(sleCacheTerm));

                    if (sleCacheBusinessLogicIndex > 0)
                        t.Insert<byte[], ulong>(stateTableName, new byte[] { 3 }, sleCacheBusinessLogicIndex);

                    sleCacheIndex = 0;
                    sleCacheTerm = 0;
                    sleCacheBusinessLogicIndex = 0;

                    t.Commit();
                }
            }
        }

        /// <summary>
        /// under lock_operation control
        /// </summary>
        /// <param name="index"></param>
        public void BusinessLogicIsApplied(ulong index)
        {
            LastBusinessLogicCommittedIndex = index;

            if (statemachine.entitySettings.DelayedPersistenceIsActive)
            {
                sleCacheBusinessLogicIndex = index;
            }
            else
            {
                using (var t = this.db.GetTransaction())
                {
                    t.Insert<byte[], ulong>(stateTableName, new byte[] { 3 }, index);
                    t.Commit();
                }
            }
        }

        /// <summary>
        /// +
        /// Can be null.
        /// Must be called inside of operation lock.
        /// get local commit history for follower
        /// </summary>
        /// <param name="logEntryId"></param>
        /// <param name="LeaderTerm"></param>
        /// <returns></returns>
        public StateLogEntrySuggestion GetNextStateLogEntrySuggestion(StateLogEntryRequest req)
        {
            StateLogEntrySuggestion le = new StateLogEntrySuggestion()
            {
                LeaderTerm = statemachine.NodeTerm
            };

            int cnt = 0;
            StateLogEntry sle = null;
            ulong prevId = 0;
            ulong prevTerm = 0;

            using (var t = this.db.GetTransaction())
            {
                if (req.StateLogEntryId == 0)// && req.StateLogEntryTerm == 0)
                {
                    var trow = t.SelectForwardFromTo<byte[], byte[]>(stateTableName,
                                 new byte[] { 1 }.ToBytes(ulong.MinValue, ulong.MinValue), true,
                                 new byte[] { 1 }.ToBytes(ulong.MaxValue, ulong.MaxValue), true).FirstOrDefault();

                    if (trow != null && trow.Exists)
                        sle = StateLogEntry.BiserDecode(trow.Value);
                    if (sle != null)
                    {
                        le.StateLogEntry = sle;
                        if (
                            LastCommittedIndexTerm >= le.StateLogEntry.Term
                            &&
                            LastCommittedIndex >= le.StateLogEntry.Index
                            )
                        {
                            le.IsCommitted = true;
                        }
                        cnt = 2;
                    }
                }
                else
                {
                    Tuple<ulong, StateLogEntry> sleTpl;
                    bool reForward = true;
                    reForward = false;
                    foreach (var el in t.SelectForwardFromTo<byte[], byte[]>(stateTableName,
                        new byte[] { 1 }.ToBytes(req.StateLogEntryId, ulong.MinValue), true,
                        new byte[] { 1 }.ToBytes(ulong.MaxValue, ulong.MaxValue), true).Take(2))
                    {
                        cnt++;
                        sle = StateLogEntry.BiserDecode(el.Value);
                        if (cnt == 1)
                        {
                            prevId = sle.Index;
                            prevTerm = sle.Term;
                        }
                        else
                        {
                            le.StateLogEntry = sle;
                        }
                    }
                    if (cnt < 2 && reForward)
                    {
                        ulong toAdd = (ulong)cnt;
                        foreach (var el in t.SelectForwardFromTo<byte[], byte[]>(stateTableName,
                        //new byte[] { 1 }.ToBytes(req.StateLogEntryId + toAdd, req.StateLogEntryTerm), true,
                        new byte[] { 1 }.ToBytes(req.StateLogEntryId + toAdd, ulong.MinValue), true,
                        new byte[] { 1 }.ToBytes(ulong.MaxValue, ulong.MaxValue), true).Take(2))
                        {
                            cnt++;
                            sle = StateLogEntry.BiserDecode(el.Value);
                            if (cnt == 1)
                            {
                                prevId = sle.Index;
                                prevTerm = sle.Term;
                            }
                            else
                            {
                                le.StateLogEntry = sle;
                            }
                        }
                    }
                    if (cnt == 2)
                    {
                        le.StateLogEntry.PreviousStateLogId = prevId;
                        le.StateLogEntry.PreviousStateLogTerm = prevTerm;
                        if (
                            LastCommittedIndexTerm >= le.StateLogEntry.Term
                            &&
                            LastCommittedIndex >= le.StateLogEntry.Index
                            )
                        {
                            le.IsCommitted = true;
                        }
                    }
                }
            }
            if (cnt != 2)
                return null;
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
                using (var t = this.db.GetTransaction())
                {
                    var row = t.Select<byte[], byte[]>(stateTableName, new byte[] { 1 }.ToBytes(logEntryId, logEntryTerm));
                    if (!row.Exists)
                    {
                        return null;
                    }
                    return StateLogEntry.BiserDecode(row.Value);
                }
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
                using (var t = this.db.GetTransaction())
                {
                    foreach (var el in t.SelectForwardFromTo<byte[], byte[]>(stateTableName,
                                new byte[] { 1 }.ToBytes(logEntryId, ulong.MinValue), true,
                                new byte[] { 1 }.ToBytes(ulong.MaxValue, ulong.MaxValue), true, true))
                    {
                        return StateLogEntry.BiserDecode(el.Value);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                throw ex;
            }

        }
        public void AddLogEntry(StateLogEntrySuggestion suggestion)

        {
            //Restoring current values
            PreviousStateLogId = suggestion.StateLogEntry.PreviousStateLogId;
            PreviousStateLogTerm = suggestion.StateLogEntry.PreviousStateLogTerm;
            StateLogId = suggestion.StateLogEntry.Index;
            StateLogTerm = suggestion.StateLogEntry.Term;
            using (var t = this.db.GetTransaction())
            {
                t.Insert<byte[], byte[]>(stateTableName, new byte[] { 1 }.ToBytes(suggestion.StateLogEntry.Index, suggestion.StateLogEntry.Term), suggestion.StateLogEntry.SerializeBiser());
                t.Commit();
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="suggestion"></param>
        /// <returns></returns>
        public void AddLogEntryByFollower(StateLogEntrySuggestion suggestion)
        {
            try
            {
                using (var t = this.db.GetTransaction())
                {
                    foreach (var el in t.SelectForwardFromTo<byte[], byte[]>(stateTableName,
                                new byte[] { 1 }.ToBytes(suggestion.StateLogEntry.Index, ulong.MinValue), true,
                                new byte[] { 1 }.ToBytes(ulong.MaxValue, ulong.MaxValue), true, true))
                    {

                        if (
                            el.Key.Substring(1, 8).To_UInt64_BigEndian() == suggestion.StateLogEntry.Index &&
                            el.Key.Substring(9, 8).To_UInt64_BigEndian() == suggestion.StateLogEntry.Term
                            )
                        {
                            return;
                        }

                        t.RemoveKey<byte[]>(stateTableName, el.Key);
                        //  pups++;
                    }
                    t.Insert<byte[], byte[]>(stateTableName, new byte[] { 1 }.ToBytes(suggestion.StateLogEntry.Index, suggestion.StateLogEntry.Term), suggestion.StateLogEntry.SerializeBiser());
                    t.Commit();
                }

                // statemachine.VerbosePrint($"{statemachine.NodeAddress.NodeAddressId}> AddToLogFollower (I/T): {suggestion.StateLogEntry.Index}/{suggestion.StateLogEntry.Term} -> Result:" +
                //    $" { (GetEntryByIndexTerm(suggestion.StateLogEntry.Index, suggestion.StateLogEntry.Term) != null)};");

                //Setting new internal LogId
                PreviousStateLogId = StateLogId;
                PreviousStateLogTerm = StateLogTerm;
                StateLogId = suggestion.StateLogEntry.Index;
                StateLogTerm = suggestion.StateLogEntry.Term;

                //tempPrevStateLogId = PreviousStateLogId;
                //tempPrevStateLogTerm = PreviousStateLogTerm;
                //tempStateLogId = StateLogId;
                //tempStateLogTerm = StateLogTerm;

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

                    }
                }
            }
            catch (Exception ex)
            {

            }

        }




        /// <summary>
        /// +
        /// Only Leader's proc.
        /// Accepts entry return true if Committed
        /// </summary>
        /// <param name="majorityNumber"></param>
        /// <param name="LogId"></param>
        /// <param name="TermId"></param>        
        public bool CommitLogEntry(NodeRaftAddress address, uint majorityQuantity, StateLogEntryApplied applied)
        {
            //If we receive acceptance signals of already Committed entries, we just ignore them

            if (this.LastCommittedIndex < applied.StateLogEntryId && statemachine.NodeTerm == applied.StateLogEntryTerm)    //Setting LastCommittedId
            {
                //Saving committed entry (all previous are automatically committed)
                List<byte[]> lstCommited = new List<byte[]>();

                using (var t = this.db.GetTransaction())
                {
                    //Gathering all not commited entries that are bigger than latest committed index
                    t.ValuesLazyLoadingIsOn = false;
                    foreach (var el in t.SelectForwardFromTo<byte[], byte[]>(stateTableName,
                       new byte[] { 1 }.ToBytes(this.LastCommittedIndex + 1, applied.StateLogEntryTerm), true,
                       new byte[] { 1 }.ToBytes(ulong.MaxValue, applied.StateLogEntryTerm), true, true))
                    {
                        lstCommited.Add(StateLogEntry.BiserDecode(el.Value).Data);
                    }

                    t.Insert<byte[], byte[]>(stateTableName, new byte[] { 2 }, applied.StateLogEntryId.ToBytes(applied.StateLogEntryTerm));
                    t.Commit();
                    //qDistribution.Remove(applied.StateLogEntryId);
                }
                this.LastCommittedIndex = applied.StateLogEntryId;
                this.LastCommittedIndexTerm = applied.StateLogEntryTerm;

                return lstCommited.Count > 0;
            }

            return false;


        }



        public void AddFakePreviousRecordForInMemoryLatestEntity(ulong prevIndex, ulong prevTerm)
        {

        }

        public void Debug_PrintOutInMemory()
        {
            Console.WriteLine("Debug_PrintOutInMemory failed - not InMemory entity");
        }

        public void ReloadFromStorage()
        {
            throw new NotImplementedException();
        }
    }
}
