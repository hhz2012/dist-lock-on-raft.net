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
    
    internal class CandidateRequest
    {
        public CandidateRequest()
        {
            TermId = 0;
            LastLogId = 0;
            LastTermId = 0;
        }

        /// <summary>
        /// 
        /// </summary>    
        public ulong TermId { get; set; }

        /// <summary>
        /// 
        /// </summary>        
        public ulong LastLogId { get; set; }

        /// <summary>
        /// 
        /// </summary>        
        public ulong LastTermId { get; set; }

     
    }
}
