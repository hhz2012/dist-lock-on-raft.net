using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.Raft
{
    public class StateMachineTimerLoop
    {
        TimeMaster TM = null;
        Random rnd = new Random();
        RaftEntitySettings entitySettings;
        RaftStateMachine stateMachine = null;
        public StateMachineTimerLoop(TimeMaster TM, RaftEntitySettings settings, RaftStateMachine stateMachine)
        {
            this.TM = TM;
            this.entitySettings = settings;
            this.stateMachine = stateMachine;
        }
        #region "TIMERS HANDLER"

        /// <summary>
        /// 
        /// </summary>
        public void RunElectionTimer()
        {
            if (this.TM.Election_TimerId == 0)
            {
                rnd.Next(System.Threading.Thread.CurrentThread.ManagedThreadId);
                int seed = rnd.Next(entitySettings.ElectionTimeoutMinMs, entitySettings.ElectionTimeoutMaxMs);
                this.TM.Election_TimerId = this.TM.FireEventEach((uint)seed, ElectionTimeout, null, true);
                this.stateMachine.VerbosePrint("Node {0} RunElectionTimer {1} ms", this.stateMachine.NodeAddress.NodeAddressId, seed);
            }
        }
        /// <summary>
        /// 
        /// </summary>
        public void RunLeaderHeartbeatWaitingTimer()
        {
            if (this.TM.LeaderHeartbeat_TimerId == 0)
                this.TM.LeaderHeartbeat_TimerId = this.TM.FireEventEach(entitySettings.LeaderHeartbeatMs, LeaderHeartbeatTimeout, null, false);
        }

        /// <summary>
        /// 
        /// </summary>
        public void RunLeaderTimer()
        {
            if (this.TM.Leader_TimerId == 0)
            {
                //Raising quickly one 
                LeaderTimerElapse(null);
                this.TM.Leader_TimerId = this.TM.FireEventEach(entitySettings.LeaderHeartbeatMs / 2, LeaderTimerElapse, null, false, "LEADER");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void RemoveLeaderTimer()
        {
            if (this.TM.Leader_TimerId > 0)
            {
                this.TM.RemoveEvent(this.TM.Leader_TimerId);
                this.TM.Leader_TimerId = 0;
            }
        }

        public void RunNoLeaderAddCommandTimer()
        {
            if (this.TM.NoLeaderAddCommand_TimerId == 0)
                this.TM.NoLeaderAddCommand_TimerId = this.TM.FireEventEach(entitySettings.NoLeaderAddCommandResendIntervalMs, (o) => {
                    this.stateMachine.logHandler.AddLogEntry(null);
                }, null, false);
        }

        public void RemoveNoLeaderAddCommandTimer()
        {
            if (this.TM.NoLeaderAddCommand_TimerId > 0)
            {
                this.TM.RemoveEvent(this.TM.NoLeaderAddCommand_TimerId);
                this.TM.NoLeaderAddCommand_TimerId = 0;
            }
        }
        public void RunLeaderLogResendTimer()
        {
            if (this.TM.LeaderLogResend_TimerId == 0)
            {
                this.TM.LeaderLogResend_TimerId = this.TM.FireEventEach(entitySettings.LeaderLogResendIntervalMs, LeaderLogResendTimerElapse, null, true);
            }
        }
        public void RemoveLeaderLogResendTimer()
        {
            if (this.TM.LeaderLogResend_TimerId > 0)
            {
                this.TM.RemoveEvent(this.TM.LeaderLogResend_TimerId);
                this.TM.LeaderLogResend_TimerId = 0;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void RemoveElectionTimer()
        {
            if (this.TM.Election_TimerId > 0)
            {
                this.TM.RemoveEvent(this.TM.Election_TimerId);
                this.TM.Election_TimerId = 0;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        public void RemoveLeaderHeartbeatWaitingTimer()
        {
            if (this.TM.LeaderHeartbeat_TimerId > 0)
            {
                this.TM.RemoveEvent(this.TM.LeaderHeartbeat_TimerId);
                this.TM.LeaderHeartbeat_TimerId = 0;
            }
        }

     
        #region leader selection loop
        /// <summary>
        /// If this action works, it can mean that Node can give a bid to be the candidate after specified time interval
        /// Starts Election timer only in case if it's not running yet
        /// </summary>
        /// <param name="userToken"></param>
        public void LeaderHeartbeatTimeout(object userToken)
        {
            try
            {
                lock (this.stateMachine.lock_Operations)
                {
                    if (this.stateMachine.NodeState == eNodeState.Leader) //me is the leader
                    {
                        RemoveLeaderHeartbeatWaitingTimer();
                        return;
                    }

                    if (DateTime.Now.Subtract(this.stateMachine.LeaderHeartbeatArrivalTime).TotalMilliseconds < this.entitySettings.LeaderHeartbeatMs)
                        return; //Early to elect, we receive completely heartbeat from the leader

                    this.stateMachine.VerbosePrint("Node {0} LeaderHeartbeatTimeout", this.stateMachine.NodeAddress.NodeAddressId);
                    RunElectionTimer();
                }
            }
            catch (Exception ex)
            {
                this.stateMachine.Log.Log(new WarningLogEntry() { Exception = ex, Method = "Raft.RaftNode.LeaderHeartbeatTimeout" });
            }

        }
        /// <summary>
        /// Time to become a candidate
        /// </summary>
        /// <param name="userToken"></param>
        public void ElectionTimeout(object userToken)
        {
            CandidateRequest req = null;
            try
            {
                lock (this.stateMachine.lock_Operations)
                {
                    if (this.TM.Election_TimerId == 0)  //Timer was switched off and we don't need to run it again
                        return;

                    this.TM.Election_TimerId = 0;

                    if (this.stateMachine.NodeState == eNodeState.Leader)
                        return;
                    this.stateMachine.VerbosePrint("Node {0} election timeout", this.stateMachine.NodeAddress.NodeAddressId);
                    this.stateMachine.NodeState = eNodeState.Candidate;
                    this.stateMachine.LeaderNodeAddress = null;
                    this.stateMachine.VerbosePrint("Node {0} state is {1} _ElectionTimeout", this.stateMachine.NodeAddress.NodeAddressId, this.stateMachine.NodeState);
                    //Voting for self
                    //VotesQuantity = 1;
                    this.stateMachine.VotesQuantity.Clear();
                    //Increasing local term number
                    this.stateMachine.NodeTerm++;
                    req = new CandidateRequest()
                    {
                        TermId = this.stateMachine.NodeTerm,
                        LastLogId = this.stateMachine.NodeStateLog.StateLogId,
                        LastTermId = this.stateMachine.NodeStateLog.StateLogTerm
                    };
                    //send to all was here
                    //Setting up new Election Timer
                    RunElectionTimer();
                }
                this.stateMachine.network.SendToAll(eRaftSignalType.CandidateRequest, req, this.stateMachine.NodeAddress, entitySettings.EntityName);
            }
            catch (Exception ex)
            {
                this.stateMachine.Log.Log(new WarningLogEntry() { Exception = ex, Method = "Raft.RaftNode.ElectionTimeout" });
            }
        }

        public void LeaderTimerElapse(object userToken)
        {
            try
            {
                //Sending signal to all (except self that it is a leader)
                LeaderHeartbeat heartBeat = null;
                lock (this.stateMachine.lock_Operations)
                {
                    heartBeat = new LeaderHeartbeat()
                    {
                        LeaderTerm = this.stateMachine.NodeTerm,
                        StateLogLatestIndex = this.stateMachine.NodeStateLog.StateLogId,
                        StateLogLatestTerm = this.stateMachine.NodeStateLog.StateLogTerm,
                        LastStateLogCommittedIndex = this.stateMachine.NodeStateLog.LastCommittedIndex,
                        LastStateLogCommittedIndexTerm = this.stateMachine.NodeStateLog.LastCommittedIndexTerm
                    };
                }
                //VerbosePrint($"{NodeAddress.NodeAddressId} (Leader)> leader_heartbeat");
                this.stateMachine.network.SendToAll(eRaftSignalType.LeaderHearthbeat, heartBeat, this.stateMachine.NodeAddress, entitySettings.EntityName, true);
            }
            catch (Exception ex)
            {
                this.stateMachine.Log.Log(new WarningLogEntry() { Exception = ex, Method = "Raft.RaftNode.LeaderTimerElapse" });
            }
        }
        public void LeaderLogResendTimerElapse(object userToken)
        {
            try
            {
                lock (this.stateMachine.lock_Operations)
                {
                    if (this.TM.LeaderLogResend_TimerId == 0)
                        return;
                    RemoveLeaderLogResendTimer();
                    this.stateMachine.InLogEntrySend = false;
                    this.stateMachine.logHandler.ApplyLogEntry();
                }

            }
            catch (Exception ex)
            {
                this.stateMachine.Log.Log(new WarningLogEntry() { Exception = ex, Method = "Raft.RaftNode.LeaderLogResendTimerElapse" });
            }
        }
        #endregion
        #endregion
    }
}
