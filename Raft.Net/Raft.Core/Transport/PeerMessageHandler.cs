using DotNetty.Transport.Channels;
using Raft.Transport;
using System;
using System.Collections.Generic;
using System.Text;

namespace Raft.Core.Transport
{
    public class PeerMessageHandler : ChannelHandlerAdapter //管道处理基类，较常用
    {
        TcpPeerNetwork connector = null;
        public PeerMessageHandler(TcpPeerNetwork connector)
        {
            this.connector = connector;
        }
        TcpPeer peer = null;
        public PeerMessageHandler(TcpPeer oldpeer)
        {
            this.peer = oldpeer;
        }

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            var str = message as string;
            if (peer == null)
            {
                peer = connector.AddTcpClient(context);
                peer.OnRecieve(context, str).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            else
            {
                peer.OnRecieve(context, str).ConfigureAwait(false).GetAwaiter().GetResult();
            }

        }
        public override void ChannelReadComplete(IChannelHandlerContext context) => context.Flush();

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            Console.WriteLine("Exception: " + exception);
            context.CloseAsync();
        }
    }
}
