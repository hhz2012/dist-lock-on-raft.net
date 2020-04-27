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

namespace Raft
{
    /// <summary>
    /// Comes from the Follower to Leader in time of state log synchronization
    /// </summary>    
    public class StateLogEntryRequest 
    {
        public StateLogEntryRequest()
        {

        }

        /// <summary>
        /// Id of the State log, which Leader must send to the Follower
        /// </summary>        
        public ulong StateLogEntryId { get; set; }
        ///// <summary>
        ///// Id of the State log, which Leader must send to the Follower
        ///// </summary>        
        //public ulong StateLogEntryTerm { get; set; }

      
    }
}
