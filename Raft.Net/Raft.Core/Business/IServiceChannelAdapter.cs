using DotNetty.Transport.Channels;
using Raft.Transport;
using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.Business
{
    public  abstract class ServiceChannelAdapter :ChannelHandlerAdapter
    {
        public abstract void SetNode(RaftServiceNode node);
    }
}
