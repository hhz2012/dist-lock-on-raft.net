/* 
  Copyright (C) 2018 tiesky.com / Alex Solovyov
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using Raft.Core;
using Raft.Core.Handler;
using Raft.Core.Raft;
using Raft.Core.StateMachine;

namespace Raft
{
    /// <summary>
    /// Main class. Initiate and Run.
    /// </summary>
    public class RaftStateMachine :IRaftStateMachine, IDisposable 
    {
       
        public readonly IWarningLog Log = null;
        public object lock_Operations = new object();
        /// <summary>
        /// Communication interface
        /// </summary>
        public IPeerConnector network = null;
        /// <summary>
        /// Node StateLog
        /// </summary>
        public IStateLog NodeStateLog = null;
        public StateMachineTimerLoop timerLoop = null;
        public StateMachineLogHandler logHandler = null;
       
        uint NodesQuantityInTheCluster = 2; //We need this value to calculate majority while leader election
        /// <summary>
        /// Current node Term
        /// </summary>
        internal ulong NodeTerm = 0;
        /// <summary>
        /// Last term this node has voted
        /// </summary>
        ulong LastVotedTermId = 0;
        /// <summary>
        /// Node settings
        /// </summary>
        internal RaftEntitySettings entitySettings = null;
        /// <summary>
        /// Address of the current node
        /// </summary>
        public NodeRaftAddress NodeAddress = new NodeRaftAddress();
        /// <summary>
        /// In case if current node is not Leader. It holds leader address
        /// </summary>
        public NodeRaftAddress LeaderNodeAddress = null;

        public string NodeName { get; set; }

        public StateMachineState States = new StateMachineState();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="dbEngine"></param>
        /// <param name="raftSender"></param>
        /// <param name="log"></param>
        /// <param name="OnCommit"></param>
        public RaftStateMachine(RaftEntitySettings settings, string workPath, IPeerConnector raftSender, IWarningLog log, IBusinessHandler handler)
        {
            this.Log = log ?? throw new Exception("Raft.Net: ILog is not supplied");
            network = raftSender;
            entitySettings = settings;         
           
            //Starting time master
            var TM = new TimeMaster(log);
            this.timerLoop = new StateMachineTimerLoop(TM, settings, this);
            
            //Starting state logger
            NodeStateLog = StateLogFactory.GetLog(this, workPath);
            this.logHandler = new StateMachineLogHandler(this, NodeStateLog, handler);
            //Adding AddLogEntryAsync cleanup
            this.timerLoop.StartClearup();
            
        }
        int disposed = 0;
        /// <summary>
        /// Starts the node
        /// </summary>
        public void NodeStart()
        {
            lock (lock_Operations)
            {
                if (States.IsRunning)
                {
                    VerbosePrint("Node {0} ALREADY RUNNING", NodeAddress.NodeAddressId);
                    return;
                }                
                VerbosePrint("Node {0} has started.", NodeAddress.NodeAddressId);
                //In the beginning node is Follower
                SetNodeFollower();
                States.IsRunning = true;
            }
            //Tries to executed not yet applied by business logic entries
            this.logHandler.Commited();
        }
        /// <summary>
        /// Stops the node
        /// </summary>
        public void NodeStop()
        {
            lock (lock_Operations)
            {
                this.timerLoop.Stop();
                this.States.NodeState = eNodeState.Follower;
                this.States.LeaderSynchronizationIsActive = false;
                //this.NodeStateLog.FlushSleCache();
                VerbosePrint("Node {0} state is {1}", NodeAddress.NodeAddressId,this.States.NodeState);
                States.IsRunning = false;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="address">Address of the node who sent the signal</param>
        /// <param name="signalType"></param>
        /// <param name="data"></param>
        public void HandleRaftSignal(NodeRaftAddress address, eRaftSignalType signalType, object data)
        {
            try
            {
                if (GlobalConfig.Verbose)
                {
                    Console.WriteLine("mesage from:" + address.EndPointSID + " signal:" + signalType);
                }
                lock (lock_Operations)
                {
                    switch (signalType)
                    {
                        case eRaftSignalType.LeaderHearthbeat:
                            ParseLeaderHeartbeat(address, data);                        
                            break;
                        case eRaftSignalType.CandidateRequest:
                            ParseCandidateRequest(address, data);
                            break;
                        case eRaftSignalType.VoteOfCandidate:
                            ParseVoteOfCandidate(address, data);
                            break;
                        case eRaftSignalType.StateLogEntrySuggestion:
                            ParseStateLogEntrySuggestion(address, data);
                            break;
                        case eRaftSignalType.StateLogEntryRequest:
                            ParseStateLogEntryRequest(address, data);
                            break;
                        case eRaftSignalType.StateLogEntryAccepted:                                                       
                            this.logHandler.ParseStateLogEntryAccepted(address, data);
                            break;
                        case eRaftSignalType.StateLogRedirectRequest: //Not a leader node tries to add command
                            this.logHandler.ParseStateLogRedirectRequest(address, data);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Log(new WarningLogEntry() { Exception = ex, Method = "Raft.RaftNode.IncomingSignalHandler" });
            }
        }
        /// <summary>
        /// Only for Leader.
        /// Follower requests new Log Entry Index from the Leader and Leader answers to the Follower
        /// </summary>
        /// <param name="address"></param>
        /// <param name="data"></param>
        void ParseStateLogEntryRequest(NodeRaftAddress address, object data)
        {
            if (this.States.NodeState != eNodeState.Leader)
                return;
            StateLogEntryRequest req = data as StateLogEntryRequest;
            //Getting suggestion
            var suggestion = this.NodeStateLog.GetNextStateLogEntrySuggestion(req);
            //VerbosePrint($"{NodeAddress.NodeAddressId} (Leader)> Request (I): {req.StateLogEntryId} from {address.NodeAddressId};");
            if (suggestion != null)
            {
                this.network.SendTo(address, eRaftSignalType.StateLogEntrySuggestion, suggestion, this.NodeAddress, entitySettings.EntityName);
            }
        }
      
        /// <summary>
        /// Only for Follower
        ///  Is called from tryCatch and in lock
        /// </summary>
        /// <param name="address"></param>
        /// <param name="data"></param>
        void ParseStateLogEntrySuggestion(NodeRaftAddress address, object data)
        {
            if (this.States.NodeState != eNodeState.Follower)
                return;

            StateLogEntrySuggestion suggest = data as StateLogEntrySuggestion; 

            if (this.NodeTerm > suggest.LeaderTerm)  //Sending Leader is not Leader anymore
            {
                this.States.LeaderSynchronizationIsActive = false;
                return;
            }

            if (this.NodeTerm < suggest.LeaderTerm)
                this.NodeTerm = suggest.LeaderTerm;
            if (suggest.StateLogEntry==null||(suggest.StateLogEntry.Index <= NodeStateLog.LastCommittedIndex)) //Preventing same entry income, can happen if restoration was sent twice (while switch of leaders)
                return;  //breakpoint don't remove

            //Checking if node can accept current suggestion
            if (suggest.StateLogEntry.PreviousStateLogId > 0)
            {
                var sle = this.NodeStateLog.GetEntryByIndexTerm(suggest.StateLogEntry.PreviousStateLogId, suggest.StateLogEntry.PreviousStateLogTerm);
                
                if (sle == null)
                {
                    
                    this.SyncronizeWithLeader();
                    return;
                    
                }                
            }          
            //We can apply new Log Entry from the Leader and answer successfully
            this.NodeStateLog.AddLogEntryByFollower(suggest);

            StateLogEntryApplied applied = new StateLogEntryApplied()
            {
                 StateLogEntryId = suggest.StateLogEntry.Index,
                 StateLogEntryTerm = suggest.StateLogEntry.Term
                // RedirectId = suggest.StateLogEntry.RedirectId
            };
            this.network.SendTo(address, eRaftSignalType.StateLogEntryAccepted, applied, this.NodeAddress, entitySettings.EntityName);          
        }

        /// <summary>
        /// Is called from tryCatch and lock.
        /// Synchronizes starting from last committed value
        /// </summary>
        /// <param name="stateLogEntryId"></param>        
        internal void SyncronizeWithLeader(bool selfCall = false)
        {
            if (!selfCall)
            {
                if (IsLeaderSynchroTimerActive)
                    return;
                NodeStateLog.RollbackToLastestCommit();
            }
            States.LeaderSynchronizationIsActive = true;
            States.LeaderSynchronizationRequestWasSent = DateTime.UtcNow;

            StateLogEntryRequest req = null;
          
            req = new StateLogEntryRequest()
            {
                StateLogEntryId = NodeStateLog.LastCommittedIndex                   
            };
            this.network.SendTo(this.LeaderNodeAddress, eRaftSignalType.StateLogEntryRequest, req, this.NodeAddress, entitySettings.EntityName);
        }
        /// <summary>
        /// All data which comes, brings TermId, if incoming TermId is bigger then current,
        /// Node updates its current termId toincoming Id and step back to the follower (if it was not).
        /// For vote and candidate requests
        /// 
        /// Must be called from lock_Operations only
        /// </summary>
        /// <param name="incomingTermId"></param>
        eTermComparationResult CompareCurrentTermWithIncoming(ulong incomingTermId)
        {
            eTermComparationResult res = eTermComparationResult.TermsAreEqual;

            if (NodeTerm < incomingTermId)
            {
                res = eTermComparationResult.CurrentTermIsSmaller;

                // Stepping back to follower state
                this.NodeTerm = incomingTermId;

                switch (this.States.NodeState)
                {
                    case eNodeState.Follower:
                        //Do nothing
                        break;
                    case eNodeState.Leader:
                        //Stepping back
                        SetNodeFollower();
                        VerbosePrint("Node {0} state is {1} _CompareCurrentTermWithIncoming", NodeAddress.NodeAddressId, this.States.NodeState);
                        break;
                    case eNodeState.Candidate:
                        //Stepping back
                        //When node is candidate, Election_TimerId always works
                        SetNodeFollower();
                        VerbosePrint("Node {0} state is {1} _CompareCurrentTermWithIncoming", NodeAddress.NodeAddressId, this.States.NodeState);                        
                        break;
                }
            }            
            else
            {
                res = eTermComparationResult.CurrentTermIsHigher;
            }

            return res;
        }
     
        /// <summary>
        /// 
        /// </summary>
        void SetNodeFollower()
        {
            if (this.States.NodeState != eNodeState.Follower)
                VerbosePrint("Node {0} state is {1} of {2}", NodeAddress.NodeAddressId, this.States.NodeState, this.LeaderNodeAddress?.NodeAddressId);
            this.States.NodeState = eNodeState.Follower;
            this.States.LeaderSynchronizationIsActive = false;
            this.timerLoop.StopLeaderTimers();
        }

        /// <summary>
        /// Is called from lock_Operations and try catch
        /// </summary>
        /// <param name="address"></param>
        /// <param name="data"></param>
        void ParseLeaderHeartbeat(NodeRaftAddress address, object data)
        {
            //var LeaderHeartbeat = data.DeserializeProtobuf<LeaderHeartbeat>();
            this.States.LeaderHeartbeat = data as LeaderHeartbeat; //data.DeserializeProtobuf<LeaderHeartbeat>();

            // Setting variable of the last heartbeat
            this.States.LeaderHeartbeatArrivalTime = DateTime.Now;
            this.LeaderNodeAddress = address;   //Can be incorrect in case if this node is Leader, must 
            //Comparing Terms
            if (this.NodeTerm < States.LeaderHeartbeat.LeaderTerm)
            {
                this.NodeTerm = States.LeaderHeartbeat.LeaderTerm;

                switch (this.States.NodeState)
                {
                    case eNodeState.Leader:
                        //Stepping back from Leader to Follower
                        SetNodeFollower();
                        VerbosePrint("Node {0} state is {1} _IncomingSignalHandler", NodeAddress.NodeAddressId, this.States.NodeState);
                        break;
                    case eNodeState.Candidate:
                        //Stepping back
                        SetNodeFollower();
                        VerbosePrint("Node {0} state is {1} _IncomingSignalHandler", NodeAddress.NodeAddressId, this.States.NodeState);
                        break;
                    case eNodeState.Follower:
                        //Ignoring
                        SetNodeFollower();  //Reseting timers
                        break;
                }
            }
            else
            {
                switch (this.States.NodeState)
                {
                    case eNodeState.Leader:
                        //2 leaders with the same Term
                        if (this.NodeTerm > States.LeaderHeartbeat.LeaderTerm)
                        {
                            //Ignoring
                            //Incoming signal is not from the Leader anymore
                            return;
                        }
                        else
                        {
                            //Stepping back                         
                            SetNodeFollower();
                            VerbosePrint("Node {0} state is {1} _IncomingSignalHandler", NodeAddress.NodeAddressId, this.States.NodeState);
                        }
                        break;
                    case eNodeState.Candidate:
                        //Stepping back
                        SetNodeFollower();
                        VerbosePrint("Node {0} state is {1} _IncomingSignalHandler", NodeAddress.NodeAddressId, this.States.NodeState);
                        break;
                    case eNodeState.Follower:
                        SetNodeFollower();
                        break;
                }
            }
            //Here will come only Followers
            this.LeaderNodeAddress = address;
            var result = this.NodeStateLog.SyncCommitByHeartBeat(this.States.LeaderHeartbeat);
            if (result.HasCommit)
            {
                this.logHandler.Commited();
            }
            if (!IsLeaderSynchroTimerActive && !result.Synced)
            {
                //VerbosePrint($"{NodeAddress.NodeAddressId}>  in sync 2 ");
                this.SyncronizeWithLeader();
            }
        }
        /// <summary>
        /// Is called from tryCatch and in lock
        /// </summary>
        /// <param name="data"></param>
        void ParseCandidateRequest(NodeRaftAddress address, object data)
        {
            var req = data as CandidateRequest; 
            VoteOfCandidate vote = new VoteOfCandidate();
            vote.VoteType = VoteOfCandidate.eVoteType.VoteFor;

            var termState = CompareCurrentTermWithIncoming(req.TermId);

            vote.TermId = NodeTerm;

            switch (termState)
            {
                case eTermComparationResult.CurrentTermIsHigher:
                    vote.VoteType = VoteOfCandidate.eVoteType.VoteReject;
                    break;
                case eTermComparationResult.CurrentTermIsSmaller:
                    //Now this Node is Follower
                    break;
            }

            if (vote.VoteType == VoteOfCandidate.eVoteType.VoteFor)
            {
                switch (this.States.NodeState)
                {
                    case eNodeState.Leader:
                        vote.VoteType = VoteOfCandidate.eVoteType.VoteReject;
                        break;
                    case eNodeState.Candidate:
                        vote.VoteType = VoteOfCandidate.eVoteType.VoteReject;
                        break;
                    case eNodeState.Follower:
                        //Probably we can vote for this Node (if we didn't vote for any other one)
                        if (LastVotedTermId < req.TermId)
                        {
                            //formula of voting
                            if (
                                (NodeStateLog.StateLogTerm > req.LastTermId)
                                ||
                                (
                                    NodeStateLog.StateLogTerm == req.LastTermId
                                    &&
                                    NodeStateLog.StateLogId > req.LastLogId
                                )
                               )
                            {
                                vote.VoteType = VoteOfCandidate.eVoteType.VoteReject;
                            }
                            else
                            {
                                LastVotedTermId = req.TermId;
                                vote.VoteType = VoteOfCandidate.eVoteType.VoteFor;

                                //Restaring Election Timer
                                this.timerLoop.StopElectionTimeLoop();
                                this.timerLoop.EnterElectionTimeLoop();
                            }
                        }
                        else
                            vote.VoteType = VoteOfCandidate.eVoteType.VoteReject;

                        break;
                }
            }

            //Sending vote signal back 
            //VerbosePrint("Node {0} voted to node {1} as {2}  _ParseCandidateRequest", NodeAddress.NodeAddressId, address.NodeAddressId, vote.VoteType);
            VerbosePrint($"Node {NodeAddress.NodeAddressId} ({this.States.NodeState}) {vote.VoteType} {address.NodeAddressId}  in  _ParseCandidateRequest");

            network.SendTo(address, eRaftSignalType.VoteOfCandidate, vote, this.NodeAddress, entitySettings.EntityName);
        }

        /// <summary>
        /// Removing from voting and from accepted entity
        /// </summary>
        /// <param name="endpointsid"></param>
        internal void PeerIsDisconnected(string endpointsid)
        {
            lock(lock_Operations)
            {
                if(States.NodeState == eNodeState.Candidate)
                {
                    States.VotesQuantity.Remove(endpointsid);
                }
                this.logHandler.Clear_dStateLogEntryAcceptance_PeerDisconnected(endpointsid);
            }
        }

        /// <summary>
        /// Node receives answer votes (to become Leader) from other nodes.
        /// Is called from tryCatch and in lock
        /// </summary>
        /// <param name="data"></param>
        void ParseVoteOfCandidate(NodeRaftAddress address, object data)
        {
            //Node received a node
            var vote = data as VoteOfCandidate;
            var termState = CompareCurrentTermWithIncoming(vote.TermId);
            if (this.States.NodeState != eNodeState.Candidate)
                return;

            switch (vote.VoteType)
            {
                case VoteOfCandidate.eVoteType.VoteFor:
                    //Calculating if node has Majority of

                    //VotesQuantity++;
                    States.VotesQuantity.Add(address.EndPointSID);

                    if ((States.VotesQuantity.Count + 1) >= this.GetMajorityQuantity())                    
                    {
                        //Majority
                        //Node becomes a Leader
                        this.States.NodeState = eNodeState.Leader;
                        //this.NodeStateLog.FlushSleCache();
                        this.logHandler.ClearLogAcceptance();
                        this.logHandler.ClearLogEntryForDistribution();

                        VerbosePrint("Node {0} state is {1} _ParseVoteOfCandidate", NodeAddress.NodeAddressId, this.States.NodeState);
                        VerbosePrint("Node {0} is Leader **********************************************",NodeAddress.NodeAddressId);
                        
                        //Stopping timers
                        this.timerLoop.StopElectionTimeLoop();
                        this.timerLoop.StopLeaderHeartbeatWaitingTimeLoop();
                                                
                        /*
                         * It's possible that we receive higher term from another leader 
                         * (in case if this leader was disconnected for some seconds from the network, 
                         * other leader can be elected and it will definitely have higher Term, so every Leader node must be ready to it)
                         */                        

                        this.timerLoop.EnterLeaderLoop();
                    }
                    //else
                    //{
                    //    //Accumulating voices
                    //    //Do nothing
                    //}

                    break;
                case VoteOfCandidate.eVoteType.VoteReject:
                    //Do nothing
                    break;               
            }
        }

        /// <summary>
        /// NodeIsInLatestState
        /// </summary>
        public bool NodeIsInLatestState
        {
            get
            {
                lock (lock_Operations)
                {
                    if (this.States.NodeState == eNodeState.Leader)
                        return true;
                    else
                    {
                        if (this.States.LeaderHeartbeat == null)
                            return false;
                        return this.NodeStateLog.LastCommittedIndex == this.States.LeaderHeartbeat.LastStateLogCommittedIndex;
                    }
                }
                    
            }
        }
        bool IsLeaderSynchroTimerActive
        {
            get
            {
                if (this.States.LeaderSynchronizationIsActive)
                {
                    if (DateTime.UtcNow.Subtract(this.States.LeaderSynchronizationRequestWasSent).TotalMinutes > this.States.LeaderSynchronizationTimeOut)
                    {
                        //Time to repeat request
                    }
                    else
                        return true; //We are already in syncrhonization mode with the Leader
                }
                return false;
            }
        }
        /// <summary>
        /// Is node a leader
        /// </summary>
        public bool IsLeader
        {
            get
            {
                return this.States.NodeState == eNodeState.Leader;
            }
        }
        /// <summary>
        /// We need this value to calculate majority while leader election
        /// </summary>
        public void SetNodesQuantityInTheCluster(uint nodesQuantityInTheCluster)
        {
            lock (lock_Operations)
            {
                NodesQuantityInTheCluster = nodesQuantityInTheCluster;
            }
        }
        public uint GetMajorityQuantity()
        {
            return (uint)Math.Floor((double)NodesQuantityInTheCluster / 2) + 1;
        }
        /// <summary>
        /// callback function to handle action ,event response
        /// </summary>

        public void Debug_PrintOutInMemory()
        {
            this.NodeStateLog.Debug_PrintOutInMemory();
        }
        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            if (System.Threading.Interlocked.CompareExchange(ref disposed, 1, 0) != 0)
                return;
            this.NodeStop();
            this.timerLoop.Dispose();
            this.NodeStateLog.Dispose();
            this.NodeStateLog = null;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="format"></param>
        /// <param name="args"></param>
        internal void VerbosePrint(string format, params object[] args)
        {
            if (GlobalConfig.Verbose)
                Console.WriteLine(String.Format("{0}> {1}", DateTime.Now.ToString("mm:ss.ms"), String.Format(format, args)));
        }
    }//eoc

}//eo ns
