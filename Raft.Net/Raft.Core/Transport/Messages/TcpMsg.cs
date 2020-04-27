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
    
    public class TcpMsg 
    {
        public TcpMsg()
        {
            MsgType = "Default";
        }
                
        public string MsgType { get; set; }
                
        public byte[] Data { get; set; }
                
        public string DataString { get; set; }

      
    }
}
