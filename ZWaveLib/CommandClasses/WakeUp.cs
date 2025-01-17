/*
    This file is part of ZWaveLib Project source code.

    ZWaveLib is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    ZWaveLib is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with ZWaveLib.  If not, see <http://www.gnu.org/licenses/>.  
*/

/*
 *     Author: Generoso Martello <gene@homegenie.it>
 *     Project Homepage: https://github.com/genielabs/zwave-lib-dotnet
 */

using System;
using System.Collections.Generic;
using System.Linq;

namespace ZWaveLib.CommandClasses
{
    public class WakeUp : ICommandClass
    {
        public CommandClass GetClassId()
        {
            return CommandClass.WakeUp;
        }

        public NodeEvent GetEvent(ZWaveNode node, byte[] message)
        {
            NodeEvent nodeEvent = null;
            byte cmdType = message[1];
            switch (cmdType)
            {
            case (byte)Command.WakeUpIntervalReport:
                if (message.Length > 4)
                {
                    uint interval = ((uint)message[2]) << 16;
                    interval |= (((uint)message[3]) << 8);
                    interval |= (uint)message[4];
                    nodeEvent = new NodeEvent(node, EventParameter.WakeUpInterval, interval, 0);
                }
                break;
            case (byte)Command.WakeUpNotification:
                // If node was marked as sleeping, reset the flag
                var wakeUpStatus = node.GetData("WakeUpStatus");
                if (wakeUpStatus != null && wakeUpStatus.Value != null && ((WakeUpStatus)wakeUpStatus.Value).IsSleeping)
                {
                    ((WakeUpStatus)wakeUpStatus.Value).IsSleeping = false;
                    var wakeEvent = new NodeEvent(node, EventParameter.WakeUpSleepingStatus, 0 /* 1 = sleeping, 0 = awake */, 0);
                    node.OnNodeUpdated(wakeEvent);
                }
                // Resend queued messages while node was asleep
                var wakeUpResendQueue = GetResendQueueData(node);
                for (int m = 0; m < wakeUpResendQueue.Count; m++)
                {
                    Utility.logger.Trace("Sending message {0} {1}", m, BitConverter.ToString(wakeUpResendQueue[m]));
                    node.SendMessage(wakeUpResendQueue[m]);
                }
                wakeUpResendQueue.Clear();
                nodeEvent = new NodeEvent(node, EventParameter.WakeUpNotify, 1, 0);
                break;
            }
            return nodeEvent;
        }

        public static ZWaveMessage Get(ZWaveNode node)
        {
            return node.SendDataRequest(new byte[] { 
                (byte)CommandClass.WakeUp, 
                (byte)Command.WakeUpIntervalGet 
            });
        }

        public static ZWaveMessage Set(ZWaveNode node, uint interval)
        {
            return node.SendDataRequest(new byte[] { 
                (byte)CommandClass.WakeUp, 
                (byte)Command.WakeUpIntervalSet,
                (byte)((interval >> 16) & 0xff),
                (byte)((interval >> 8) & 0xff),
                (byte)((interval) & 0xff),
                0x01
            });
        }

        public static void ResendOnWakeUp(ZWaveNode node, byte[] msg)
        {
            int minCommandLength = 8;
            if (msg.Length >= minCommandLength)
            {
                byte[] command = new byte[minCommandLength];
                Array.Copy(msg, 0, command, 0, minCommandLength);
                // discard any message having same header and command (first 8 bytes = header + command class + command)
                var wakeUpResendQueue = GetResendQueueData(node);
                for (int i = wakeUpResendQueue.Count - 1; i >= 0; i--)
                {
                    byte[] queuedCommand = new byte[minCommandLength];
                    Array.Copy(wakeUpResendQueue[i], 0, queuedCommand, 0, minCommandLength);
                    if (queuedCommand.SequenceEqual(command))
                    {
                        Utility.logger.Trace("Removing old message {0}", BitConverter.ToString(wakeUpResendQueue[i]));
                        wakeUpResendQueue.RemoveAt(i);
                    }
                }
                Utility.logger.Trace("Adding message {0}", BitConverter.ToString(msg));
                wakeUpResendQueue.Add(msg);
                var wakeUpStatus = (WakeUpStatus)node.GetData("WakeUpStatus", new WakeUpStatus()).Value;
                if (!wakeUpStatus.IsSleeping)
                {
                    wakeUpStatus.IsSleeping = true;
                    var nodeEvent = new NodeEvent(node, EventParameter.WakeUpSleepingStatus, 1 /* 1 = sleeping, 0 = awake */, 0);
                    node.OnNodeUpdated(nodeEvent);
                }
            }
        }

        private static List<byte[]> GetResendQueueData(ZWaveNode node)
        {
            return (List<byte[]>)node.GetData("WakeUpResendQueue", new List<byte[]>()).Value;
        }
    }
}

