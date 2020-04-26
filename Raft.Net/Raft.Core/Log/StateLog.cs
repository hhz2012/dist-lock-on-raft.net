﻿/* 
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
        class StateLogEntryAcceptance
        {
            public StateLogEntryAcceptance()
            {
                //Quantity = 0;
            }

            /// </summary>
            public ulong Index { get; set; }
            /// <summary>
            /// StateLogEntry Term
            /// </summary>
            public ulong Term { get; set; }

            public HashSet<string> acceptedEndPoints = new HashSet<string>();

        }

        /// <summary>
        /// Main table that stores logs
        /// </summary>
        string tblStateLogEntry = "RaftTbl_StateLogEntry";

        ///// <summary>
        ///// Leader only. Stores logs before being distributed.
        ///// </summary>

        internal RaftStateMachine rn = null;


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

        SortedDictionary<ulong, Tuple<ulong, StateLogEntry>> sleCache = new SortedDictionary<ulong, Tuple<ulong, StateLogEntry>>();
        ulong sleCacheIndex = 0;
        ulong sleCacheTerm = 0;
        ulong sleCacheBusinessLogicIndex = 0;


        IndexTermDict<StateLogEntry> inMem = new IndexTermDict<StateLogEntry>();
        DBreezeEngine db = null;
        public StateLog(RaftStateMachine rn, DBreezeEngine dbEngine)
        {
            this.rn = rn;
            this.db = dbEngine;

            if (rn.entitySettings.EntityName != "default")
                tblStateLogEntry += "_" + rn.entitySettings.EntityName;

            if (rn.entitySettings.InMemoryEntity)
                tblStateLogEntry = "mem_" + tblStateLogEntry;

            using (var t = this.db.GetTransaction())
            {
                var row = t.SelectBackwardFromTo<byte[], byte[]>(tblStateLogEntry,
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

                    tempPrevStateLogId = PreviousStateLogId;
                    tempPrevStateLogTerm = PreviousStateLogTerm;
                    tempStateLogId = StateLogId;
                    tempStateLogTerm = StateLogTerm;

                    rn.NodeTerm = sle.Term;
                }
                var rowTerm = t.Select<byte[], byte[]>(tblStateLogEntry, new byte[] { 2 });
                if (rowTerm.Exists)
                {
                    LastCommittedIndex = rowTerm.Value.Substring(0, 8).To_UInt64_BigEndian();
                    LastCommittedIndexTerm = rowTerm.Value.Substring(8, 8).To_UInt64_BigEndian();
                }

                var rowBL = t.Select<byte[], ulong>(tblStateLogEntry, new byte[] { 3 });
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
        /// Returns null if nothing to distribute
        /// </summary>
        /// <returns></returns>
        StateLogEntrySuggestion GetNextLogEntryToBeDistributed()
        {
            if (qDistribution.Count < 1)
                return null;

            return new StateLogEntrySuggestion()
            {
                StateLogEntry = qDistribution.OrderBy(r => r.Key).First().Value,
                LeaderTerm = rn.NodeTerm
            };
        }
        ulong tempPrevStateLogId = 0;
        ulong tempPrevStateLogTerm = 0;
        ulong tempStateLogId = 0;
        ulong tempStateLogTerm = 0;

        /// <summary>
        /// Leader only.Stores logs before being distributed.
        /// </summary>       
        SortedDictionary<ulong, StateLogEntry> qDistribution = new SortedDictionary<ulong, StateLogEntry>();

        /// <summary>
        /// Is called from lock_operations
        /// Adds to silo table, until is moved to log table.
        /// This table can be cleared up on start
        /// returns concatenated term+index inserted identifier
        /// </summary>
        /// <param name="data"></param>
        /// <param name="externalID">if set up must be returned in OnCommitted to notify that command is executed</param>
        /// <returns></returns>
        public void AddStateLogEntryForDistribution(byte[] data, byte[] externalID = null)
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
        }

        /// <summary>
        /// When Node is selected as leader it is cleared
        /// </summary>
        public void ClearLogEntryForDistribution()
        {
            qDistribution.Clear();
        }
        /// <summary>
        /// under lock_operations
        /// Copyies from distribution silo table and puts in StateLog table       
        /// </summary>
        /// <returns></returns>
        public StateLogEntrySuggestion AddNextEntryToStateLogByLeader()
        {
            var suggest = GetNextLogEntryToBeDistributed();
            if (suggest == null)
                return null;

            //Restoring current values
            PreviousStateLogId = suggest.StateLogEntry.PreviousStateLogId;
            PreviousStateLogTerm = suggest.StateLogEntry.PreviousStateLogTerm;
            StateLogId = suggest.StateLogEntry.Index;
            StateLogTerm = suggest.StateLogEntry.Term;
            using (var t = this.db.GetTransaction())
            {
                t.Insert<byte[], byte[]>(tblStateLogEntry, new byte[] { 1 }.ToBytes(suggest.StateLogEntry.Index, suggest.StateLogEntry.Term), suggest.StateLogEntry.SerializeBiser());
                t.Commit();
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
                Tuple<ulong, StateLogEntry> sleTpl;
                using (var t = this.db.GetTransaction())
                {

                    t.ValuesLazyLoadingIsOn = false;
                    var row = t.Select<byte[], byte[]>(tblStateLogEntry, (new byte[] { 1 }).ToBytes(lhb.LastStateLogCommittedIndex, lhb.LastStateLogCommittedIndexTerm));
                    if (row.Exists)
                    {
                        populateFrom = this.LastCommittedIndex + 1;

                        this.LastCommittedIndex = lhb.LastStateLogCommittedIndex;
                        this.LastCommittedIndexTerm = lhb.LastStateLogCommittedIndexTerm;

                        //rn.VerbosePrint($"{rn.NodeAddress.NodeAddressId}> AddToLogFollower (I/T): here");

                        t.Insert<byte[], byte[]>(tblStateLogEntry, new byte[] { 2 }, lhb.LastStateLogCommittedIndex.ToBytes(lhb.LastStateLogCommittedIndexTerm));
                        t.Commit();
                    }
                    else
                        return false;
                }
                if (populateFrom > 0)
                    this.rn.Commited();
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

            FlushSleCache();
            using (var t = this.db.GetTransaction())
            {
                //Removing from the persisted all keys equal or bigger then suppled Log
                foreach (var el in t.SelectForwardFromTo<byte[], byte[]>(tblStateLogEntry,
                            new byte[] { 1 }.ToBytes(LastCommittedIndex, LastCommittedIndexTerm), false,
                            new byte[] { 1 }.ToBytes(ulong.MaxValue, ulong.MaxValue), true, true))
                {
                    t.RemoveKey<byte[]>(tblStateLogEntry, el.Key);
                }

                t.Commit();
            }
        }

        public void FlushSleCache()
        {
            if (!rn.entitySettings.DelayedPersistenceIsActive)
                return;
            if (sleCache.Count > 0 || sleCacheIndex > 0 || sleCacheTerm > 0 || sleCacheBusinessLogicIndex > 0)
            {
                rn.VerbosePrint($"{rn.NodeAddress.NodeAddressId}> flushing: {sleCache.Count}");

                using (var t = this.db.GetTransaction())
                {
                    if (sleCache.Count > 0)
                    {
                        foreach (var el in sleCache)
                            t.Insert<byte[], byte[]>(tblStateLogEntry, new byte[] { 1 }.ToBytes(el.Key, el.Value.Item1), el.Value.Item2.SerializeBiser());

                        sleCache.Clear();
                    }

                    if (sleCacheIndex > 0 && sleCacheTerm > 0)
                        t.Insert<byte[], byte[]>(tblStateLogEntry, new byte[] { 2 }, sleCacheIndex.ToBytes(sleCacheTerm));

                    if (sleCacheBusinessLogicIndex > 0)
                        t.Insert<byte[], ulong>(tblStateLogEntry, new byte[] { 3 }, sleCacheBusinessLogicIndex);

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

            if (rn.entitySettings.DelayedPersistenceIsActive)
            {
                sleCacheBusinessLogicIndex = index;
            }
            else
            {
                using (var t = this.db.GetTransaction())
                {
                    t.Insert<byte[], ulong>(tblStateLogEntry, new byte[] { 3 }, index);
                    t.Commit();
                }
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

            int cnt = 0;
            StateLogEntry sle = null;
            ulong prevId = 0;
            ulong prevTerm = 0;

            using (var t = this.db.GetTransaction())
            {

                if (req.StateLogEntryId == 0)// && req.StateLogEntryTerm == 0)
                {
                    if (rn.entitySettings.DelayedPersistenceIsActive && sleCache.Count > 0)
                    {
                        sle = sleCache.OrderBy(r => r.Key).First().Value.Item2;
                    }
                    else
                    {
                        var trow = t.SelectForwardFromTo<byte[], byte[]>(tblStateLogEntry,
                               new byte[] { 1 }.ToBytes(ulong.MinValue, ulong.MinValue), true,
                               new byte[] { 1 }.ToBytes(ulong.MaxValue, ulong.MaxValue), true).FirstOrDefault();

                        if (trow != null && trow.Exists)
                            sle = StateLogEntry.BiserDecode(trow.Value);
                    }
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

                    if (rn.entitySettings.DelayedPersistenceIsActive &&
                        sleCache.TryGetValue(req.StateLogEntryId, out sleTpl)
                        )
                    {
                        cnt++;
                        sle = sleTpl.Item2;
                        prevId = sle.Index;
                        prevTerm = sle.Term;

                        if (sleCache.TryGetValue(req.StateLogEntryId + 1, out sleTpl))
                        {
                            cnt++;
                            sle = sleTpl.Item2;
                            le.StateLogEntry = sle;
                        }
                    }
                    else
                    {
                        reForward = false;
                        foreach (var el in t.SelectForwardFromTo<byte[], byte[]>(tblStateLogEntry,
                            //new byte[] { 1 }.ToBytes(req.StateLogEntryId, req.StateLogEntryTerm), true,
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
                    }
                    if (cnt < 2 && reForward)
                    {
                        ulong toAdd = (ulong)cnt;
                        foreach (var el in t.SelectForwardFromTo<byte[], byte[]>(tblStateLogEntry,
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
                    var row = t.Select<byte[], byte[]>(tblStateLogEntry, new byte[] { 1 }.ToBytes(logEntryId, logEntryTerm));
                    if (!row.Exists)
                    {
                        //foreach (var el in t.SelectForwardStartsWith<byte[], byte[]>(tblStateLogEntry, new byte[] { 1 }))
                        //{
                        //    Console.WriteLine($"{rn.NodeAddress.NodeAddressId}> GetEntryByIndexTerm-NULL: {el.Key.ToBytesString()}");
                        //}
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
                    foreach (var el in t.SelectForwardFromTo<byte[], byte[]>(tblStateLogEntry,
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


        /// <summary>
        /// 
        /// </summary>
        /// <param name="suggestion"></param>
        /// <returns></returns>
        public void AddToLogFollower(StateLogEntrySuggestion suggestion)
        {
            try
            {
                using (var t = this.db.GetTransaction())
                {
                    foreach (var el in t.SelectForwardFromTo<byte[], byte[]>(tblStateLogEntry,
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

                        t.RemoveKey<byte[]>(tblStateLogEntry, el.Key);
                        //  pups++;
                    }
                    t.Insert<byte[], byte[]>(tblStateLogEntry, new byte[] { 1 }.ToBytes(suggestion.StateLogEntry.Index, suggestion.StateLogEntry.Term), suggestion.StateLogEntry.SerializeBiser());
                    t.Commit();
                }

                rn.VerbosePrint($"{rn.NodeAddress.NodeAddressId}> AddToLogFollower (I/T): {suggestion.StateLogEntry.Index}/{suggestion.StateLogEntry.Term} -> Result:" +
                    $" { (GetEntryByIndexTerm(suggestion.StateLogEntry.Index, suggestion.StateLogEntry.Term) != null)};");

                //Setting new internal LogId
                PreviousStateLogId = StateLogId;
                PreviousStateLogTerm = StateLogTerm;
                StateLogId = suggestion.StateLogEntry.Index;
                StateLogTerm = suggestion.StateLogEntry.Term;

                tempPrevStateLogId = PreviousStateLogId;
                tempPrevStateLogTerm = PreviousStateLogTerm;
                tempStateLogId = StateLogId;
                tempStateLogTerm = StateLogTerm;

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
                        this.rn.Commited();
                    }
                }
            }
            catch (Exception ex)
            {

            }
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

        public enum eEntryAcceptanceResult
        {
            NotAccepted,
            Committed,
            AlreadyAccepted,
            Accepted
        }
        /// <summary>
        /// +
        /// Only Leader's proc.
        /// Accepts entry return true if Committed
        /// </summary>
        /// <param name="majorityNumber"></param>
        /// <param name="LogId"></param>
        /// <param name="TermId"></param>        
        public eEntryAcceptanceResult EntryIsAccepted(NodeAddress address, uint majorityQuantity, StateLogEntryApplied applied)
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

                //acc.Quantity += 1;              
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

                    using (var t = this.db.GetTransaction())
                    {
                        //Gathering all not commited entries that are bigger than latest committed index
                        t.ValuesLazyLoadingIsOn = false;
                        foreach (var el in t.SelectForwardFromTo<byte[], byte[]>(tblStateLogEntry,
                           new byte[] { 1 }.ToBytes(this.LastCommittedIndex + 1, applied.StateLogEntryTerm), true,
                           new byte[] { 1 }.ToBytes(ulong.MaxValue, applied.StateLogEntryTerm), true, true))
                        {
                            lstCommited.Add(StateLogEntry.BiserDecode(el.Value).Data);
                        }

                        t.Insert<byte[], byte[]>(tblStateLogEntry, new byte[] { 2 }, applied.StateLogEntryId.ToBytes(applied.StateLogEntryTerm));
                        t.Commit();
                        qDistribution.Remove(applied.StateLogEntryId);
                    }
                    this.LastCommittedIndex = applied.StateLogEntryId;
                    this.LastCommittedIndexTerm = applied.StateLogEntryTerm;

                    if (lstCommited.Count > 0)
                        this.rn.Commited();
                    return eEntryAcceptanceResult.Committed;
                }
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

    }
}
