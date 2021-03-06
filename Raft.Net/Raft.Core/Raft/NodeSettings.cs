﻿using Raft.Transport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DBreeze.Utils;

namespace Raft
{
    public class NodeSettings
    {
        /// <summary>
        /// List of Raft Entities that will use one TCP Transport to get the same state for all entites
        /// </summary>
        public RaftEntitySettings RaftEntitiesSettings { get; set; } = new RaftEntitySettings();
        /// <summary>
        /// Quantity of Cluster EndPoints is used to get majority of servers to Commit Entity
        /// </summary>
        public List<PeerEndPoint> TcpClusterEndPoints { get; set; } = new List<PeerEndPoint>();

        

    }//eoc
}//eon
