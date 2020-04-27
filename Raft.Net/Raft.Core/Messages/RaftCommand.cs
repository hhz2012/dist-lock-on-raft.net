using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core
{
    public class RaftCommand
    {
        public const int Handshake = 1;
        public const int RaftMessage = 2;
        public const int HandshakeACK = 3;
        public const int FreeMessage = 4;
        public const int Ping = 5;
        public int Code { get; set; }
        public object Message { get; set; }
    }
    
}
