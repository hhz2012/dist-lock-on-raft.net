
using System;
using Biser;
using System.Collections.Generic;
using System.Text;
using RocksDbSharp;
using System.ComponentModel;

namespace Raft.Core.LogStore
{
    public class RocksDBLogStore : IStateLog
    {
        RaftStateMachine stateMachine = null;
        RocksDb db = null;
        public RocksDBLogStore(RaftStateMachine rn,string path)
        {
           
            this.stateMachine = rn;
            string logdbFile = path + "_logdb.db";
            var options = new DbOptions()
            .SetCreateIfMissing(true);
            db = RocksDb.Open(options, logdbFile);
        }
        public ulong StateLogId { get; set; }
        public ulong StateLogTerm { get; set; }
        public ulong LastCommittedIndex { get; set; }
        public ulong LastCommittedIndexTerm { get; set; }
        public ulong LastBusinessLogicCommittedIndex { get; set; }
        public ulong LastAppliedIndex { get; set; }

        public ulong PreviousStateLogId = 0;
        public ulong PreviousStateLogTerm = 0;

       

        public void AddFakePreviousRecordForInMemoryLatestEntity(ulong prevIndex, ulong prevTerm)
        {
            throw new NotImplementedException();
        }
        public byte[] GetKey(ulong term, ulong index)
        {
            byte[] v1=BitConverter.GetBytes(index);
            byte[] v2 = BitConverter.GetBytes(index);
            byte[] key = new byte[v1.Length + v2.Length];
            for (int i = 0; i < v1.Length; i++) key[i] = v1[i];
            for (int i = 0; i < v1.Length; i++) key[i+v1.Length] = v2[i];
            return key;

        }
        public void AddLogEntry(StateLogEntrySuggestion suggestion)
        {
            PreviousStateLogId = suggestion.StateLogEntry.PreviousStateLogId;
            PreviousStateLogTerm = suggestion.StateLogEntry.PreviousStateLogTerm;
            StateLogId = suggestion.StateLogEntry.Index;
            StateLogTerm = suggestion.StateLogEntry.Term;
            var key = GetKey(suggestion.StateLogEntry.Term, suggestion.StateLogEntry.Index);
            var value= db.Get(key);
            var find = value != null;
            if (find)
            {
                var entry = StateLogEntry.BiserDecode(value);
                entry.IsCommitted = suggestion.IsCommitted;
                value = entry.BiserEncoder().Encode();
                db.Put(key, value);
            }
            else
            {
                var data = suggestion.StateLogEntry.BiserEncode();
                db.Put(key,data);
            }
        }

        public void AddLogEntryByFollower(StateLogEntrySuggestion suggestion)
        {
            //remove all log bigger than this,(clear no committed logs)
            var key = GetKey(suggestion.StateLogEntry.Term, suggestion.StateLogEntry.Index);
            var iter = db.NewIterator();
            iter.Seek(key);
            while(iter.Valid())
            {
                iter.Next();
                if (iter.Valid())
                {
                    db.Remove(iter.Key());
                }
                else break;
            }
            //add this one
            AddLogEntry(suggestion);
            //update commit status
            if (suggestion.IsCommitted)
            {
                if (this.LastCommittedIndexTerm > suggestion.StateLogEntry.Term
                    ||(
                        this.LastCommittedIndexTerm == suggestion.StateLogEntry.Term
                        &&this.LastCommittedIndex > suggestion.StateLogEntry.Index
                       ))
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

        public bool CommitLogEntry(NodeRaftAddress address, uint majorityQuantity, StateLogEntryApplied applied)
        {
            //If we receive acceptance signals of already Committed entries, we just ignore them
            if (this.LastCommittedIndex < applied.StateLogEntryId && stateMachine.NodeTerm == applied.StateLogEntryTerm)    //Setting LastCommittedId
            {
                var key = GetKey(applied.StateLogEntryTerm, applied.StateLogEntryId);
                var iter = db.NewIterator();
                iter.Seek(key);
                int update = 0;
                while(iter.Valid())
                {
                    var entry = StateLogEntry.BiserDecode(iter.Value());
                    if (entry.IsCommitted) break;
                    entry.IsCommitted = true;
                    var value = entry.BiserEncode();
                    db.Put(key, value);
                    update++;
                    iter.Prev();
                }
                this.LastCommittedIndex = applied.StateLogEntryId;
                this.LastCommittedIndexTerm = applied.StateLogEntryTerm;
                return update > 0;
                
            }
            return false;
        }

        public void Debug_PrintOutInMemory()
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
        }

        public StateLogEntry GetCommitedEntryByIndex(ulong logEntryTerm,ulong logEntryId)
        {
            var key = GetKey(logEntryTerm, logEntryId);

            var value = db.Get(key);
            var find = value == null;
            if (find)
            {
                var entry = StateLogEntry.BiserDecode(value);
                return entry;
            }
            return null;
        }

        public StateLogEntry GetEntryByIndexTerm(ulong logEntryId, ulong logEntryTerm)
        {
            var key = GetKey(logEntryTerm, logEntryId);
            
            var value = db.Get(key);
            var find = value == null;
            if (find)
            {
                var entry = StateLogEntry.BiserDecode(value);
                return entry;
            }
            return null;
        }

        public StateLogEntrySuggestion GetNextStateLogEntrySuggestion(StateLogEntryRequest req)
        {
            StateLogEntrySuggestion le = new StateLogEntrySuggestion()
            {
                LeaderTerm = this.stateMachine.NodeTerm
            };
            ulong prevId = 0;
            ulong prevTerm = 0;
            StateLogEntry entry = null;
            if (req.StateLogEntryId == 0)
            {
                //send first record to sync
                var key = GetKey(0, 0);
                var iter = db.NewIterator();
                iter.Seek(key);
                iter.Next();
                while(iter.Valid())
                {
                    entry = StateLogEntry.BiserDecode(iter.Value());
                    if (entry.IsCommitted) break;
                    iter.Next();
                }
            }
            else
            {
                var key = GetKey(req.StateLogEntryTerm, req.StateLogEntryId);
                
                var iter = db.NewIterator();
                iter.Seek(key);
                var lastOne = iter.Value();
                iter.Next();
                while (iter.Valid())
                {
                    entry = StateLogEntry.BiserDecode(iter.Value());
                    if (entry.Index>req.StateLogEntryId)
                    {
                        var oldEntry = StateLogEntry.BiserDecode(lastOne);
                        prevId = oldEntry.Index;
                        prevTerm = oldEntry.Term;
                        
                    }
                }
            }
            if (entry != null)
            {
                le.StateLogEntry = entry;
                entry.PreviousStateLogId = prevId;
                entry.PreviousStateLogTerm = prevTerm;
                le.IsCommitted = entry.IsCommitted;
                return le;
            }
            else return null;

        }

        public void ReloadFromStorage()
        {
            //should load from log files
           

        }

        public void RollbackToLastestCommit()
        {
            if (LastCommittedIndex == 0 || LastCommittedIndexTerm == 0)
                return;
            var key = GetKey(0, 0);
            
            var iter = db.NewIterator();
            iter.SeekToLast();
            while(iter.Valid())
            {
                var entry = StateLogEntry.BiserDecode(iter.Value());
                if (entry.Term >= LastCommittedIndexTerm&&entry.Index>LastCommittedIndex)
                {
                    db.Remove(iter.Key());
                }
            }
        }

        public SyncResult SyncCommitByHeartBeat(LeaderHeartbeat lhb)
        {
            if (GlobalConfig.Verbose)
            {
                Console.WriteLine($"leader info:{lhb.LastStateLogCommittedIndex} ,mine:{this.LastCommittedIndex}");
            }
                if (this.LastCommittedIndex < lhb.LastStateLogCommittedIndex)
            {

                //find if this entry exist 
                var key = GetKey(lhb.LastStateLogCommittedIndexTerm, lhb.StateLogLatestIndex);
                var value = db.Get(key);
                var find = value == null;
                if (find)
                {
                    var entry = StateLogEntry.BiserDecode(value);
                    if (!entry.IsCommitted)
                    {
                        entry.IsCommitted = true;
                        value = entry.BiserEncode();
                        db.Put( key, value);
                        this.LastCommittedIndex = lhb.LastStateLogCommittedIndex;
                        return new SyncResult() { HasCommit = true, Synced = true };
                    }
                    else
                    {
                        return new SyncResult() { HasCommit = false, Synced = true };
                    }
                }
                else
                {
                    return new SyncResult() { HasCommit = false, Synced = false };
                }
            }
            else
            {
                return new SyncResult() { HasCommit = false, Synced = true };
            }
        }
    }

      
    }
