using DBreeze.Utils;
using Raft.Core.Handler;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raft.Core.Raft
{
    public class StateMachineLogHandler
    {
        RaftStateMachine stateMachine;
        IBusinessHandler handler;
        ulong tempPrevStateLogId = 0;
        ulong tempPrevStateLogTerm = 0;
        ulong tempStateLogId = 0;
        ulong tempStateLogTerm = 0;
        //Tuple of iData and externalId of that data (formed by node to receive info back that this command is added)
        public Queue<Tuple<byte[], byte[]>> NoLeaderCache = new Queue<Tuple<byte[], byte[]>>();

        public StateMachineLogHandler(RaftStateMachine stateMachine, IBusinessHandler handler)
        {
            this.stateMachine = stateMachine;
            this.handler = handler;
        }

        /// <summary>
        /// Is called from lock_operations
        /// Tries to apply new entry, must be called from lock
        /// </summary>
        public void EnqueueAndDistrbuteLog()
        {
            if (this.stateMachine.States.InLogEntrySend)
                return;

            var suggest = this.moveDistributeToStore();
            if (suggest == null)
                return;

            //VerbosePrint($"{NodeAddress.NodeAddressId} (Leader)> Sending to all (I/T): {suggest.StateLogEntry.Index}/{suggest.StateLogEntry.Term};");

            this.stateMachine.States.InLogEntrySend = true;
            this.stateMachine.timerLoop.RunLeaderLogResendTimer();
            this.stateMachine.network.SendToAll(eRaftSignalType.StateLogEntrySuggestion, suggest, this.stateMachine.NodeAddress, this.stateMachine.entitySettings.EntityName);
        }
        /// <summary>
        /// Leader and followers via redirect. (later callback info for followers is needed)
        /// </summary>
        /// <param name="data"></param>
        /// <param name="logEntryExternalId"></param>
        /// <returns></returns>
        public AddLogEntryResult ProcessAddLogRequest(byte[] iData, byte[] externalId = null)
        {
            AddLogEntryResult res = new AddLogEntryResult();

            try
            {
                lock (this.stateMachine.lock_Operations)
                {
                    if (iData != null)
                        this.NoLeaderCache.Enqueue(new Tuple<byte[], byte[]>(iData, externalId));

                    if (this.stateMachine.States.NodeState == eNodeState.Leader)
                    {
                        this.stateMachine.timerLoop.RemoveNoLeaderAddCommandTimer();

                        while (this.NoLeaderCache.Count > 0)
                        {
                            var nlc = this.NoLeaderCache.Dequeue();
                            this.AddStateLogEntryForDistribution(nlc.Item1, nlc.Item2);
                            EnqueueAndDistrbuteLog();
                        }
                        res.LeaderAddress = this.stateMachine.NodeAddress;
                        res.AddResult = AddLogEntryResult.eAddLogEntryResult.LOG_ENTRY_IS_CACHED;
                    }
                    else
                    {
                        if (this.stateMachine.LeaderNodeAddress == null)
                        {
                            res.AddResult = AddLogEntryResult.eAddLogEntryResult.NO_LEADER_YET;
                            this.stateMachine.timerLoop.RunNoLeaderAddCommandTimer();
                        }
                        else
                        {
                            this.stateMachine.timerLoop.RemoveNoLeaderAddCommandTimer();
                            res.AddResult = AddLogEntryResult.eAddLogEntryResult.NODE_NOT_A_LEADER;
                            res.LeaderAddress = this.stateMachine.LeaderNodeAddress;

                            //Redirecting only in case if there is a leader                            
                            while (this.NoLeaderCache.Count > 0)
                            {
                                var nlc = this.NoLeaderCache.Dequeue();
                                this.stateMachine.network.SendTo(this.stateMachine.LeaderNodeAddress, eRaftSignalType.StateLogRedirectRequest,
                                (
                                    new StateLogEntryRedirectRequest
                                    {
                                        Data = nlc.Item1,
                                        ExternalID = nlc.Item2
                                    }
                                ), this.stateMachine.NodeAddress, this.stateMachine.entitySettings.EntityName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.stateMachine.Log.Log(new WarningLogEntry() { Exception = ex, Method = "Raft.RaftNode.AddLogEntryLeader" });
                res.AddResult = AddLogEntryResult.eAddLogEntryResult.ERROR_OCCURED;
            }
            return res;
        }
        internal void Commited()
        {
            if (System.Threading.Interlocked.CompareExchange(ref this.stateMachine.States.inCommit, 1, 0) != 0)
                return;
            Task.Run(() =>
            {
                StateLogEntry sle = null;
                while (true)
                {
                    lock (this.stateMachine.lock_Operations)
                    {
                        if (this.stateMachine.NodeStateLog.LastCommittedIndex == this.stateMachine.NodeStateLog.LastBusinessLogicCommittedIndex)
                        {
                            System.Threading.Interlocked.Exchange(ref this.stateMachine.States.inCommit, 0);
                            return;
                        }
                        else
                        {
                            sle = this.stateMachine.NodeStateLog.GetCommitedEntryByIndex(this.stateMachine.NodeStateLog.LastBusinessLogicCommittedIndex + 1);
                            if (sle == null)
                            {
                                System.Threading.Interlocked.Exchange(ref this.stateMachine.States.inCommit, 0);
                                return;
                            }
                        }
                    }

                    try
                    {
                        if (this.stateMachine.handler.DoAction(this.stateMachine.entitySettings.EntityName, sle.Index, sle.Data))
                        {
                            //In case if business logic commit was successful
                            lock (this.stateMachine.lock_Operations)
                            {
                                this.stateMachine.NodeStateLog.BusinessLogicIsApplied(sle.Index);
                            }
                            //Notifying Async AddLog
                            if (sle.ExternalID != null && AsyncResponseHandler.df.TryGetValue(sle.ExternalID.ToBytesString(), out var responseCrate))
                            {
                                responseCrate.IsRespOk = true;
                                responseCrate.res = sle.ExternalID;
                                responseCrate.Set_MRE();
                            }
                        }
                        else
                        {
                            System.Threading.Thread.Sleep(500);
                            //repeating with the same id
                        }
                    }
                    catch (Exception ex)
                    {
                        this.stateMachine.Log.Log(new WarningLogEntry() { Exception = ex, Method = "Raft.RaftNode.Commited" });
                        //Notifying Async AddLog
                        if (sle.ExternalID != null && AsyncResponseHandler.df.TryGetValue(sle.ExternalID.ToBytesString(), out var responseCrate))
                        {
                            responseCrate.IsRespOk = false;
                            responseCrate.res = sle.ExternalID;
                            responseCrate.Set_MRE();
                        }
                    }
                }
            });
        }
        /// <summary>
        /// Leader receives accepted Log
        /// </summary>
        /// <param name="address"></param>
        /// <param name="data"></param>
        public void ParseStateLogEntryAccepted(NodeRaftAddress address, object data)
        {
            if (this.stateMachine.States.NodeState != eNodeState.Leader)
                return;

            StateLogEntryApplied applied = data as StateLogEntryApplied;

            var res = this.stateMachine.NodeStateLog.EntryIsAccepted(address, this.stateMachine.GetMajorityQuantity(), applied);

            if (res == eEntryAcceptanceResult.Committed)
            {
                qDistribution.Remove(applied.StateLogEntryId);
                //this.VerbosePrint($"{this.NodeAddress.NodeAddressId}> LogEntry {applied.StateLogEntryId} is COMMITTED (answer from {address.NodeAddressId})"+DateTime.Now.Second+":"+DateTime.Now.Millisecond);
                this.stateMachine.timerLoop.RemoveLeaderLogResendTimer();
                //Force heartbeat, to make followers to get faster info about commited elements
                LeaderHeartbeat heartBeat = new LeaderHeartbeat()
                {
                    LeaderTerm = this.stateMachine.NodeTerm,
                    StateLogLatestIndex = this.stateMachine.NodeStateLog.StateLogId,
                    StateLogLatestTerm = this.stateMachine.NodeStateLog.StateLogTerm,
                    LastStateLogCommittedIndex = this.stateMachine.NodeStateLog.LastCommittedIndex,
                    LastStateLogCommittedIndexTerm = this.stateMachine.NodeStateLog.LastCommittedIndexTerm
                };
                this.stateMachine.network.SendToAll(eRaftSignalType.LeaderHearthbeat, heartBeat, this.stateMachine.NodeAddress, this.stateMachine.entitySettings.EntityName, true);
                //---------------------------------------
                this.stateMachine.States.InLogEntrySend = false;
                EnqueueAndDistrbuteLog();
            }
        }
        /// <summary>
        /// called from lock try..catch
        /// </summary>
        /// <param name="address"></param>
        /// <param name="data"></param>
        public void ParseStateLogRedirectRequest(NodeRaftAddress address, object data)
        {
            StateLogEntryRedirectRequest req = data as StateLogEntryRedirectRequest;

            if (this.stateMachine.States.NodeState != eNodeState.Leader)  //Just return
                return;

            this.AddStateLogEntryForDistribution(req.Data, req.ExternalID);//, redirectId);
            this.EnqueueAndDistrbuteLog();

            //Don't answer, committed value wil be delivered via standard channel           
        }

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
        public StateLogEntry AddStateLogEntryForDistribution(byte[] data, byte[] externalID = null)
        {
            /*
             * Only nodes of the current term can be distributed
             */

            tempPrevStateLogId = tempStateLogId;
            tempPrevStateLogTerm = tempStateLogTerm;
            tempStateLogId++;
            tempStateLogTerm = this.stateMachine.NodeTerm;

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
        /// under lock_operations
        /// Copyies from distribution silo table and puts in StateLog table       
        /// </summary>
        /// <returns></returns>
        public StateLogEntrySuggestion moveDistributeToStore()
        {
            var suggest = GetNextLogEntryToBeDistributed();
            if (suggest == null)
                return null;

            //Restoring current values
            //PreviousStateLogId = suggest.StateLogEntry.PreviousStateLogId;
            //PreviousStateLogTerm = suggest.StateLogEntry.PreviousStateLogTerm;
            //StateLogId = suggest.StateLogEntry.Index;
            //StateLogTerm = suggest.StateLogEntry.Term;
            //using (var t = this.db.GetTransaction())
            //{
            //    t.Insert<byte[], byte[]>(stateTableName, new byte[] { 1 }.ToBytes(suggest.StateLogEntry.Index, suggest.StateLogEntry.Term), suggest.StateLogEntry.SerializeBiser());
            //    t.Commit();
            //}
            return suggest;
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
                LeaderTerm = this.stateMachine.NodeTerm
            };
        }
    }
}
