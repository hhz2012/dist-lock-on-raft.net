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
    public class StateLogEntrySuggestion 
    {
        public StateLogEntrySuggestion()
        {

        }

        /// <summary>
        /// Current leader TermId, must be always included
        /// </summary> 
        public ulong LeaderTerm { get; set; }
                
        public StateLogEntry StateLogEntry { get; set; }
                
        public bool IsCommitted { get; set; } = false;

      
    }
}
