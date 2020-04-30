using DBreeze.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Raft.Core.Raft
{
    public class StateMachineLogHandler
    {
        RaftStateMachine stateMachine;
        public StateMachineLogHandler(RaftStateMachine stateMachine)
        {
            this.stateMachine = stateMachine;
        }

        /// <summary>
        /// Is called from lock_operations
        /// Tries to apply new entry, must be called from lock
        /// </summary>
        public void ApplyLogEntry()
        {
            if (this.stateMachine.InLogEntrySend)
                return;

            var suggest = this.stateMachine.NodeStateLog.AddNextEntryToStateLogByLeader();
            if (suggest == null)
                return;

            //VerbosePrint($"{NodeAddress.NodeAddressId} (Leader)> Sending to all (I/T): {suggest.StateLogEntry.Index}/{suggest.StateLogEntry.Term};");

            this.stateMachine.InLogEntrySend = true;
            this.stateMachine.loop.RunLeaderLogResendTimer();
            this.stateMachine.network.SendToAll(eRaftSignalType.StateLogEntrySuggestion, suggest, this.stateMachine.NodeAddress, this.stateMachine.entitySettings.EntityName);
        }
        /// <summary>
        /// Leader and followers via redirect. (later callback info for followers is needed)
        /// </summary>
        /// <param name="data"></param>
        /// <param name="logEntryExternalId"></param>
        /// <returns></returns>
        public AddLogEntryResult AddLogEntry(byte[] iData, byte[] externalId = null)
        {
            AddLogEntryResult res = new AddLogEntryResult();

            try
            {
                lock (this.stateMachine.lock_Operations)
                {
                    if (iData != null)
                        this.stateMachine.NoLeaderCache.Enqueue(new Tuple<byte[], byte[]>(iData, externalId));

                    if (this.stateMachine.NodeState == eNodeState.Leader)
                    {
                        this.stateMachine.loop.RemoveNoLeaderAddCommandTimer();

                        while (this.stateMachine.NoLeaderCache.Count > 0)
                        {
                            var nlc = this.stateMachine.NoLeaderCache.Dequeue();
                            this.stateMachine.NodeStateLog.AddStateLogEntryForDistribution(nlc.Item1, nlc.Item2);
                            ApplyLogEntry();
                        }
                        res.LeaderAddress = this.stateMachine.NodeAddress;
                        res.AddResult = AddLogEntryResult.eAddLogEntryResult.LOG_ENTRY_IS_CACHED;
                    }
                    else
                    {
                        if (this.stateMachine.LeaderNodeAddress == null)
                        {
                            res.AddResult = AddLogEntryResult.eAddLogEntryResult.NO_LEADER_YET;
                            this.stateMachine.loop.RunNoLeaderAddCommandTimer();
                        }
                        else
                        {
                            this.stateMachine.loop.RemoveNoLeaderAddCommandTimer();
                            res.AddResult = AddLogEntryResult.eAddLogEntryResult.NODE_NOT_A_LEADER;
                            res.LeaderAddress = this.stateMachine.LeaderNodeAddress;

                            //Redirecting only in case if there is a leader                            
                            while (this.stateMachine.NoLeaderCache.Count > 0)
                            {
                                var nlc = this.stateMachine.NoLeaderCache.Dequeue();
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
            if (System.Threading.Interlocked.CompareExchange(ref this.stateMachine.inCommit, 1, 0) != 0)
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
                            System.Threading.Interlocked.Exchange(ref this.stateMachine.inCommit, 0);
                            return;
                        }
                        else
                        {
                            sle = this.stateMachine.NodeStateLog.GetCommitedEntryByIndex(this.stateMachine.NodeStateLog.LastBusinessLogicCommittedIndex + 1);
                            if (sle == null)
                            {
                                System.Threading.Interlocked.Exchange(ref this.stateMachine.inCommit, 0);
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
            if (this.stateMachine.NodeState != eNodeState.Leader)
                return;

            StateLogEntryApplied applied = data as StateLogEntryApplied;

            var res = this.stateMachine.NodeStateLog.EntryIsAccepted(address, this.stateMachine.GetMajorityQuantity(), applied);

            if (res == eEntryAcceptanceResult.Committed)
            {
                //this.VerbosePrint($"{this.NodeAddress.NodeAddressId}> LogEntry {applied.StateLogEntryId} is COMMITTED (answer from {address.NodeAddressId})"+DateTime.Now.Second+":"+DateTime.Now.Millisecond);
                this.stateMachine.loop.RemoveLeaderLogResendTimer();
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
                this.stateMachine.InLogEntrySend = false;
                ApplyLogEntry();
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

            if (this.stateMachine.NodeState != eNodeState.Leader)  //Just return
                return;

            this.stateMachine.NodeStateLog.AddStateLogEntryForDistribution(req.Data, req.ExternalID);//, redirectId);
            this.ApplyLogEntry();

            //Don't answer, committed value wil be delivered via standard channel           
        }

    }
}
