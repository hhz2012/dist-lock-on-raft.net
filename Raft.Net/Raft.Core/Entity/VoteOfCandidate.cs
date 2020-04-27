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
    internal class VoteOfCandidate 
    {
        public enum eVoteType
        {
            VoteFor,           
            VoteReject
        }
        public VoteOfCandidate()
        {
            TermId = 0;
            VoteType = eVoteType.VoteFor;
        }

        /// <summary>
        /// 
        /// </summary>       
        public ulong TermId { get; set; }

        /// <summary>
        /// 
        /// </summary>        
        public eVoteType VoteType { get; set; }


        
    }
}
