/* 
  Copyright (C) 2018 tiesky.com / Alex Solovyov
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using DBreeze.Utils;

namespace Raft.Transport
{    
    public class PeerEndPoint 
    { 
        public string Host { get; set; } = "127.0.0.1";
             
        public int Port { get; set; } = 4320;
                
        public string EndPointSID { get { return Host + ":" + Port; } }
       
        internal TcpPeer Peer { get; set; } = null;


     
    }
}
