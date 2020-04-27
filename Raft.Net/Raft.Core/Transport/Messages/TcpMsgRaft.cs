/* 
  Copyright (C) 2018 tiesky.com / Alex Solovyov
  It's a free software for those, who think that it should be free.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using DBreeze.Utils;

namespace Raft.Transport
{    
    public class TcpMsgRaft 
    {
        public TcpMsgRaft()
        {

        }

        /// <summary>
        /// from Raft.eRaftSignalType
        /// </summary>      
        public eRaftSignalType RaftSignalType { get; set; }
                
        public object Data { get; set; }

        public string EntityName { get; set; } = "default";
    
    }

}
