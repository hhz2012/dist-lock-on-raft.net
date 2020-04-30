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

        /// <summary>
        /// Supplied via constructor. Will be called and supply
        /// </summary>
        //Func<string, ulong, byte[],RaftNode, bool> OnCommit = null;
        public IActionHandler handler;

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
        /// Address of the current node
        /// </summary>
        public NodeRaftAddress NodeAddress = new NodeRaftAddress();
        /// <summary>
        /// In case if current node is not Leader. It holds leader address
        /// </summary>
        public NodeRaftAddress LeaderNodeAddress = null;
        /// <summary>
        /// Received Current leader heartbeat time
        /// </summary>
        public DateTime LeaderHeartbeatArrivalTime = DateTime.MinValue;
        /// <summary>
        /// Latest heartbeat from leader, can be null on start
        /// </summary>
        internal LeaderHeartbeat LeaderHeartbeat = null;
        public HashSet<string> VotesQuantity = new HashSet<string>();
        //state 
        public eNodeState NodeState = eNodeState.Follower;
        /// <summary>
        /// Is node started 
        /// </summary>
        public bool IsRunning = false;
        /// <summary>
        /// Node settings
        /// </summary>
        internal RaftEntitySettings entitySettings = null;
        public string NodeName { get; set; }

        public int inCommit = 0;
        public bool InLogEntrySend = false;
        //Tuple of iData and externalId of that data (formed by node to receive info back that this command is added)
        public Queue<Tuple<byte[], byte[]>> NoLeaderCache = new Queue<Tuple<byte[], byte[]>>();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="settings"></param>
        /// <param name="dbEngine"></param>
        /// <param name="raftSender"></param>
        /// <param name="log"></param>
        /// <param name="OnCommit"></param>
        public RaftStateMachine(RaftEntitySettings settings, string workPath, IPeerConnector raftSender, IWarningLog log, IActionHandler handler)
        {
            this.Log = log ?? throw new Exception("Raft.Net: ILog is not supplied");
            this.handler = handler;
            this.handler.SetNode(this);
            network = raftSender;
            entitySettings = settings;         
           
            //Starting time master
            var TM = new TimeMaster(log);
            this.timerLoop = new StateMachineTimerLoop(TM, settings, this);
            this.logHandler = new StateMachineLogHandler(this);
            //Starting state logger
            NodeStateLog = StateLogFactory.GetLog(this, workPath);
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
                if (IsRunning)
                {
                    VerbosePrint("Node {0} ALREADY RUNNING", NodeAddress.NodeAddressId);
                    return;
                }                
                VerbosePrint("Node {0} has started.", NodeAddress.NodeAddressId);
                //In the beginning node is Follower
                SetNodeFollower();
                IsRunning = true;
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
                this.NodeState = eNodeState.Follower;
                this.NodeStateLog.LeaderSynchronizationIsActive = false;
                //this.NodeStateLog.FlushSleCache();
                VerbosePrint("Node {0} state is {1}", NodeAddress.NodeAddressId,this.NodeState);
                IsRunning = false;
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
            if (this.NodeState != eNodeState.Leader)
                return;
            StateLogEntryRequest req = data as StateLogEntryRequest;
            //Getting suggestion
            var suggestion = this.NodeStateLog.GetNextStateLogEntrySuggestionFromRequested(req);
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
            if (this.NodeState != eNodeState.Follower)
                return;

            StateLogEntrySuggestion suggest = data as StateLogEntrySuggestion; 

            if (this.NodeTerm > suggest.LeaderTerm)  //Sending Leader is not Leader anymore
            {
                this.NodeStateLog.LeaderSynchronizationIsActive = false;
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
                    //We don't have previous to this log and need new index request   
                    //VerbosePrint($"{NodeAddress.NodeAddressId}>  in sync 1 ");
                    if (entitySettings.InMemoryEntity && entitySettings.InMemoryEntityStartSyncFromLatestEntity && this.NodeStateLog.LastAppliedIndex == 0)
                    {
                        //helps newly starting mode with specific InMemory parameters get only latest command for the entity
                        this.NodeStateLog.AddFakePreviousRecordForInMemoryLatestEntity(suggest.StateLogEntry.PreviousStateLogId, suggest.StateLogEntry.PreviousStateLogTerm);

                    }
                    else
                    {
                        this.SyncronizeWithLeader();
                        return;
                    }
                }                
            }          
            //We can apply new Log Entry from the Leader and answer successfully
            this.NodeStateLog.AddToLogFollower(suggest);

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
                NodeStateLog.ClearStateLogStartingFromCommitted();
            }
            NodeStateLog.LeaderSynchronizationIsActive = true;
            NodeStateLog.LeaderSynchronizationRequestWasSent = DateTime.UtcNow;

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

                switch (this.NodeState)
                {
                    case eNodeState.Follower:
                        //Do nothing
                        break;
                    case eNodeState.Leader:
                        //Stepping back
                        SetNodeFollower();
                        VerbosePrint("Node {0} state is {1} _CompareCurrentTermWithIncoming", NodeAddress.NodeAddressId, this.NodeState);
                        break;
                    case eNodeState.Candidate:
                        //Stepping back
                        //When node is candidate, Election_TimerId always works
                        SetNodeFollower();
                        VerbosePrint("Node {0} state is {1} _CompareCurrentTermWithIncoming", NodeAddress.NodeAddressId, this.NodeState);                        
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
            if (this.NodeState != eNodeState.Follower)
                VerbosePrint("Node {0} state is {1} of {2}", NodeAddress.NodeAddressId, this.NodeState, this.LeaderNodeAddress?.NodeAddressId);
            this.NodeState = eNodeState.Follower;
            this.NodeStateLog.LeaderSynchronizationIsActive = false;
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
            this.LeaderHeartbeat = data as LeaderHeartbeat; //data.DeserializeProtobuf<LeaderHeartbeat>();

            // Setting variable of the last heartbeat
            this.LeaderHeartbeatArrivalTime = DateTime.Now;
            this.LeaderNodeAddress = address;   //Can be incorrect in case if this node is Leader, must 
            //Comparing Terms
            if (this.NodeTerm < LeaderHeartbeat.LeaderTerm)
            {
                this.NodeTerm = LeaderHeartbeat.LeaderTerm;

                switch (this.NodeState)
                {
                    case eNodeState.Leader:
                        //Stepping back from Leader to Follower
                        SetNodeFollower();
                        VerbosePrint("Node {0} state is {1} _IncomingSignalHandler", NodeAddress.NodeAddressId, this.NodeState);
                        break;
                    case eNodeState.Candidate:
                        //Stepping back
                        SetNodeFollower();
                        VerbosePrint("Node {0} state is {1} _IncomingSignalHandler", NodeAddress.NodeAddressId, this.NodeState);
                        break;
                    case eNodeState.Follower:
                        //Ignoring
                        SetNodeFollower();  //Reseting timers
                        break;
                }
            }
            else
            {
                switch (this.NodeState)
                {
                    case eNodeState.Leader:
                        //2 leaders with the same Term
                        if (this.NodeTerm > LeaderHeartbeat.LeaderTerm)
                        {
                            //Ignoring
                            //Incoming signal is not from the Leader anymore
                            return;
                        }
                        else
                        {
                            //Stepping back                         
                            SetNodeFollower();
                            VerbosePrint("Node {0} state is {1} _IncomingSignalHandler", NodeAddress.NodeAddressId, this.NodeState);
                        }
                        break;
                    case eNodeState.Candidate:
                        //Stepping back
                        SetNodeFollower();
                        VerbosePrint("Node {0} state is {1} _IncomingSignalHandler", NodeAddress.NodeAddressId, this.NodeState);
                        break;
                    case eNodeState.Follower:
                        SetNodeFollower();
                        break;
                }
            }
            //Here will come only Followers
            this.LeaderNodeAddress = address;
            if (!IsLeaderSynchroTimerActive && !this.NodeStateLog.SetLastCommittedIndexFromLeader(LeaderHeartbeat))
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
                switch (this.NodeState)
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
                                this.timerLoop.RemoveElectionTimer();
                                this.timerLoop.RunElectionTimer();
                            }
                        }
                        else
                            vote.VoteType = VoteOfCandidate.eVoteType.VoteReject;

                        break;
                }
            }

            //Sending vote signal back 
            //VerbosePrint("Node {0} voted to node {1} as {2}  _ParseCandidateRequest", NodeAddress.NodeAddressId, address.NodeAddressId, vote.VoteType);
            VerbosePrint($"Node {NodeAddress.NodeAddressId} ({this.NodeState}) {vote.VoteType} {address.NodeAddressId}  in  _ParseCandidateRequest");

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
                if(NodeState == eNodeState.Candidate)
                {
                    VotesQuantity.Remove(endpointsid);
                }
                NodeStateLog.Clear_dStateLogEntryAcceptance_PeerDisconnected(endpointsid);
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
            if (this.NodeState != eNodeState.Candidate)
                return;

            switch (vote.VoteType)
            {
                case VoteOfCandidate.eVoteType.VoteFor:
                    //Calculating if node has Majority of
                   
                    //VotesQuantity++;
                    VotesQuantity.Add(address.EndPointSID);

                    if ((VotesQuantity.Count + 1) >= this.GetMajorityQuantity())                    
                    {
                        //Majority
                        //Node becomes a Leader
                        this.NodeState = eNodeState.Leader;
                        //this.NodeStateLog.FlushSleCache();
                        this.NodeStateLog.ClearLogAcceptance();
                        this.NodeStateLog.ClearLogEntryForDistribution();

                        VerbosePrint("Node {0} state is {1} _ParseVoteOfCandidate", NodeAddress.NodeAddressId, this.NodeState);
                        VerbosePrint("Node {0} is Leader **********************************************",NodeAddress.NodeAddressId);
                        
                        //Stopping timers
                        this.timerLoop.RemoveElectionTimer();
                        this.timerLoop.RemoveLeaderHeartbeatWaitingTimer();
                                                
                        /*
                         * It's possible that we receive higher term from another leader 
                         * (in case if this leader was disconnected for some seconds from the network, 
                         * other leader can be elected and it will definitely have higher Term, so every Leader node must be ready to it)
                         */                        

                        this.timerLoop.RunLeaderTimer();
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
                    if (this.NodeState == eNodeState.Leader)
                        return true;
                    else
                    {
                        if (this.LeaderHeartbeat == null)
                            return false;
                        return this.NodeStateLog.LastCommittedIndex == this.LeaderHeartbeat.LastStateLogCommittedIndex;
                    }
                }
                    
            }
        }
        bool IsLeaderSynchroTimerActive
        {
            get
            {
                if (this.NodeStateLog.LeaderSynchronizationIsActive)
                {
                    if (DateTime.UtcNow.Subtract(this.NodeStateLog.LeaderSynchronizationRequestWasSent).TotalMinutes > this.NodeStateLog.LeaderSynchronizationTimeOut)
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
                return this.NodeState == eNodeState.Leader;
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
