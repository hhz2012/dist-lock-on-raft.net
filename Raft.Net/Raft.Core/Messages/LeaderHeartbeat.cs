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
    public class LeaderHeartbeat
    {
        public LeaderHeartbeat()
        {
            LeaderTerm = 0;
        }

        /// <summary>
        /// Leader's current Term
        /// </summary>        
        public ulong LeaderTerm { get; set; }

        /// <summary>
        /// Latest inserted into Leader State Log Term
        /// </summary>        
        public ulong StateLogLatestTerm { get; set; }

        /// <summary>
        /// Latest inserted into Leader State Log ID
        /// </summary>        
        public ulong StateLogLatestIndex { get; set; }

        /// <summary>
        /// Leader includes LastCommitted Index, Followers must apply if it's bigger than theirs
        /// </summary>        
        public ulong LastStateLogCommittedIndex { get; set; }

        /// <summary>
        /// Leader includes LastCommitted Term, Followers must apply if it's bigger than theirs
        /// </summary>        
        public ulong LastStateLogCommittedIndexTerm { get; set; }

    }
}
