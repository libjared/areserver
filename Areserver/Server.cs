using System;
using System.Collections.Generic;
using Lidgren.Network;
using System.Threading;
using System.Linq;
using System.Reflection;

namespace Areserver
{
    public class Server
    {
        public static string commandBuffer = string.Empty;
        public static NetServer server;

        public const int MapWidth = 20;
        public const int MapHeight = 20;
        public const int MapDepth = 3;
        public static List<Actor> dActors;
        public static List<GameObject> dGameObjects;
        public static Tile[,,] dTiles;
        public static Wall[,,] dWallsLeft;
        public static Wall[,,] dWallsTop;

        public static void Main(string[] args)
        {
            Out("Welcome to the Areserver");
            SetupReadLine();
            InitLocalData();
            SetupServer();

            while (true)
            {
                HandleLidgrenMessages();
                System.Threading.Thread.Sleep(8);
            }
        }

        private static void InitLocalData()
        {
            dActors = new List<Actor>();
            GenerateMap();
        }

        public static void GenerateMap()
        {
            dTiles = new Tile[MapWidth, MapHeight, MapDepth];
            dWallsLeft = new Wall[MapWidth + 1, MapHeight, MapDepth];
            dWallsTop = new Wall[MapWidth, MapHeight + 1, MapDepth];
            for (int z = 0; z < MapDepth; z++)
            {
                for (int y = 0; y < MapHeight; y++)
                {
                    for (int x = 0; x < MapWidth; x++)
                    {
                        dTiles[x, y, z] = new WoodTile();
                    }
                }
            }
            HardcodeWalls();
        }

        private static void HardcodeWalls()
        {
            for (int z = 0; z < MapDepth; z++)
            {
                for (int y = 0; y < 20; y++) //left side
                {
                    dWallsLeft[0, y, z] = new RedBrickWall(true);
                }

                for (int y = 0; y < 20; y++) //right side
                {
                    dWallsLeft[20, y, z] = new RedBrickWall(true);
                }

                for (int x = 0; x < 20; x++) //top side
                {
                    dWallsTop[x, 0, z] = new RedBrickWall(false);
                }
                for (int x = 0; x < 20; x++) //top side
                {
                    dWallsTop[x, 20, z] = new RedBrickWall(false);
                }
            }

            //little square
            dWallsTop[10, 10, 0] = new RedBrickWall(false);
            dWallsTop[10, 11, 0] = new RedBrickWall(false);
            dWallsLeft[10, 10, 0] = new RedBrickWall(true);

            dWallsTop[10-5, 10-5, 2] = new RedBrickWall(false);
            dWallsTop[10-5, 11-5, 2] = new RedBrickWall(false);
            dWallsLeft[10-5, 10-5, 2] = new RedBrickWall(true);

            //door
            dWallsLeft[0, 1, 0] = new WoodDoor(true);
        }

        private static void SetupServer()
        {
            NetPeerConfiguration config = new NetPeerConfiguration("ares");
            config.MaximumConnections = 32;
            config.Port = 12345;
            config.EnableMessageType(NetIncomingMessageType.ConnectionLatencyUpdated);
            server = new NetServer(config);
            server.Start();
        }

        private static void HandleLidgrenMessages()
        {
            NetIncomingMessage msg;
            while ((msg = server.ReadMessage()) != null)
            {
                switch (msg.MessageType)
                {
                    case NetIncomingMessageType.VerboseDebugMessage:
                    case NetIncomingMessageType.DebugMessage:
                    case NetIncomingMessageType.WarningMessage:
                    case NetIncomingMessageType.ErrorMessage:
                        Out(msg.ReadString());
                        break;
                    case NetIncomingMessageType.StatusChanged:
                        var newStatus = (NetConnectionStatus)msg.ReadByte();
                        if (newStatus == NetConnectionStatus.Connected)
                        {
                            OnConnect(msg);
                        }
                        else if (newStatus == NetConnectionStatus.Disconnected)
                        {
                            OnDisconnect(msg);
                        }
                        break;
                    case NetIncomingMessageType.Data:
                        HandleGameMessage(msg);
                        break;
                    case NetIncomingMessageType.ConnectionLatencyUpdated:
					    //TODO: ping? pong!
                        break;
                    default:
                        Out(string.Format("Unhandled type: {0}", msg.MessageType));
                        break;
                }
                server.Recycle(msg);
            }
        }

        private static void OnConnect(NetIncomingMessage msg)
        {
            //tell everyone else he joined
            {
                NetOutgoingMessage outMsg = server.CreateMessage();
                outMsg.Write("JOIN");
                outMsg.Write(msg.SenderConnection.RemoteUniqueIdentifier);
                server.SendToAll(outMsg, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
            }

            Out(string.Format("JOIN: {0}", msg.SenderConnection.RemoteUniqueIdentifier));

            InformNewbieState(msg);

            //intial data finished sending; add him to the player list, tag his Player for easy access
            Player thisPlayer = new Player(msg.SenderConnection);
            thisPlayer.UID = msg.SenderConnection.RemoteUniqueIdentifier;
            dActors.Add(thisPlayer);
            msg.SenderConnection.Tag = thisPlayer;
        }

        private static void InformNewbieState(NetIncomingMessage msg)
        {
            NetOutgoingMessage newbieState = server.CreateMessage();

            newbieState.Write("MULTI_ON");

            foreach (var actor in dActors) //not using server.Connections
            {
                Player plr = (Player)actor;
                newbieState.Write("JOIN");
                newbieState.Write(plr.UID);

                newbieState.Write("NAME");
                newbieState.Write(plr.UID); //long uid
                newbieState.Write(plr.Name); //string name

                newbieState.Write("LIFE");
                newbieState.Write(plr.UID);
                newbieState.Write(plr.Life);
            }

            AppendMapSnapshot(newbieState);

            newbieState.Write("MULTI_OFF");

            server.SendMessage(newbieState, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered);
        }

        public static void AppendMapSnapshot(NetOutgoingMessage outgoing)
        {
            //lots of TILE
            for (int z = 0; z < MapDepth; z++)
            {
                for (int y = 0; y < MapHeight; y++)
                {
                    for (int x = 0; x < MapWidth; x++)
                    {
                        Tile tileHere = dTiles[x, y, z];
                        if (tileHere == null)
                            continue;
                        outgoing.Write("TILE");
                        outgoing.Write(x);
                        outgoing.Write(y);
                        outgoing.Write(z);
                        outgoing.Write(tileHere.TileID);
                    }
                }
            }
            //lots of left WALL
            for (int z = 0; z < MapDepth; z++)
            {
                for (int y = 0; y < dWallsLeft.GetLength(1); y++)
                {
                    for (int x = 0; x < dWallsLeft.GetLength(0); x++)
                    {
                        Wall leftHere = dWallsLeft[x, y, z];
                        if (leftHere == null)
                            continue;
                        outgoing.Write("WALL");
                        outgoing.Write(x);
                        outgoing.Write(y);
                        outgoing.Write(z);
                        outgoing.Write(leftHere.WallID);
                        outgoing.Write(true);
                    }
                }
            }
            //lots of top WALL
            for (int z = 0; z < MapDepth; z++)
            {
                for (int y = 0; y < dWallsTop.GetLength(1); y++)
                {
                    for (int x = 0; x < dWallsTop.GetLength(0); x++)
                    {
                        Wall topHere = dWallsTop[x, y, z];
                        if (topHere == null)
                            continue;
                        outgoing.Write("WALL");
                        outgoing.Write(x);
                        outgoing.Write(y);
                        outgoing.Write(z);
                        outgoing.Write(topHere.WallID);
                        outgoing.Write(false);
                    }
                }
            }
        }

        private static void OnDisconnect(NetIncomingMessage msg)
        {
            NetOutgoingMessage outMsg = server.CreateMessage();
            outMsg.Write("PART");
            outMsg.Write(msg.SenderConnection.RemoteUniqueIdentifier);

            server.SendToAll(outMsg, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
            Out(string.Format("PART: {0}", msg.SenderConnection.RemoteUniqueIdentifier));

            //remove datas
            dActors.Remove((Player)msg.SenderConnection.Tag);
        }

        private static void HandleGameMessage(NetIncomingMessage msg)
        {
            string type = msg.ReadString();

            switch (type)
            {
                case "POS":
                    HandlePOS(msg);
                    break;
                case "LIFE":
                    HandleLIFE(msg);
                    break;
                case "CHAT":
                    HandleCHAT(msg);
                    break;
                case "NAME":
                    HandleNAME(msg);
                    break;
                default:
                    Out(string.Format("Bad message type {0} from player {1}",
                        type, msg.SenderConnection.RemoteUniqueIdentifier));
                    break;
            }
        }

        public static Player GetPlayerFromUID(long uid)
        {
            foreach (var actor in dActors)
            {
                if (actor.GetType() != typeof(Player))
                    throw new Exception("FIXME found an actor that's not a player");
                Player plr = (Player)actor;
                if (plr.UID == uid)
                {
                    return plr;
                }
            }
            return null;
        }

        #region HandleX
        static void HandlePOS(NetIncomingMessage msg)
        {
            int newX = msg.ReadInt32();
            int newY = msg.ReadInt32();
            int newZ = msg.ReadInt32();

            //save position
            Player plr = GetPlayerFromUID(msg.SenderConnection.RemoteUniqueIdentifier);
            plr.X = newX;
            plr.Y = newY;
            plr.Z = newZ;

            //inform ALL clients about position change
            NetOutgoingMessage outMsg = server.CreateMessage();
            outMsg.Write("POS");
            outMsg.Write(msg.SenderConnection.RemoteUniqueIdentifier);
            outMsg.Write(newX);
            outMsg.Write(newY);
            outMsg.Write(newZ);
            server.SendToAll(outMsg, null, NetDeliveryMethod.ReliableOrdered, 0);
        }

        static void HandleLIFE(NetIncomingMessage msg)
        {
            //no longer boolean
            int newHp = msg.ReadInt32();
            Out(string.Format("LIFE: {0}: {1}", msg.SenderConnection.RemoteUniqueIdentifier, newHp));

            //save value
            GetPlayerFromUID(msg.SenderConnection.RemoteUniqueIdentifier).Life = newHp;

            //inform ALL clients about his pining for the fjords
            NetOutgoingMessage outMsgLife = server.CreateMessage();
            outMsgLife.Write("LIFE");
            outMsgLife.Write(msg.SenderConnection.RemoteUniqueIdentifier);
            outMsgLife.Write(newHp);
            server.SendToAll(outMsgLife, null, NetDeliveryMethod.ReliableOrdered, 0);
        }

        static void HandleCHAT(NetIncomingMessage msg)
        {
            string message = msg.ReadString();
            Out(string.Format("CHAT: {0}: {1}", msg.SenderConnection.RemoteUniqueIdentifier, message));

            //send the chat to ALL clients
            NetOutgoingMessage outMsg = server.CreateMessage();
            outMsg.Write("CHAT");
            outMsg.Write(msg.SenderConnection.RemoteUniqueIdentifier);
            outMsg.Write(message);
            server.SendToAll(outMsg, null, NetDeliveryMethod.ReliableOrdered, 0);
        }

        static void HandleNAME(NetIncomingMessage msg)
        {
            string newName = msg.ReadString();
            string oldName = GetPlayerFromUID(msg.SenderConnection.RemoteUniqueIdentifier).Name;
            Out(string.Format("NAME: {0} changed {1}", oldName, newName));

            //save name in dict
            GetPlayerFromUID(msg.SenderConnection.RemoteUniqueIdentifier).Name = newName;

            //inform ALL clients about his name change
            NetOutgoingMessage outMsg = server.CreateMessage();
            outMsg.Write("NAME");
            outMsg.Write(msg.SenderConnection.RemoteUniqueIdentifier);
            outMsg.Write(newName);
            server.SendToAll(outMsg, null, NetDeliveryMethod.ReliableOrdered, 0);
        }
        #endregion

        #region Console Commands
        private static void SetupReadLine()
        {
            Thread t = new Thread(() => {
                while (true)
                {
                    var inpC = (char)Console.Read();

                    //corner cases: \n, \r, \t, \0, \b
                    switch (inpC)
                    {
                        case '\n': //command done, do it (enter key linux)
                        case '\r': //(enter key windows)
                            string cmd = commandBuffer;
                            commandBuffer = string.Empty;
                            HandleCommand(cmd);
                            break;
                        case '\t': //do nothing TODO: tab completion
                            break;
                        case '\0': //erase a char (backspace monodevelop, gnome-terminal)
                        case '\b': //(no known platforms use this)
                            //cygwin sshd, mintty, win32 all line-buffer, so BS is not ever encountered
                            if (commandBuffer == string.Empty) //ignore when line already cleared
                                break;
                            commandBuffer = commandBuffer.Substring(0, commandBuffer.Length - 1);
                            RedrawCommandBuffer();
                            break;
                        default:   //add it, because regular char
                            commandBuffer += inpC;
                            break;
                    }
                }
            });
            t.Start();
        }

        private static void RedrawCommandBuffer()
        {
            //redraw the command buffer
            var seventyNineSpaces = string.Concat(Enumerable.Repeat(" ", Console.BufferWidth - 1));
            Console.Write("\r");
            Console.Write(seventyNineSpaces);
            Console.Write("\r");
            Console.Write(commandBuffer);
        }

        private static void HandleCommand(string thisCmd)
        {
            string[] cmdArgsAll = thisCmd.Split(' ');
            string[] cmdArgs = new string[cmdArgsAll.Length - 1];
            Array.Copy(cmdArgsAll, 1, cmdArgs, 0, cmdArgs.Length);

            if (!ExecCommand(cmdArgsAll[0], cmdArgs))
            {
                Out("unrecognized cmd");
            }
        }

        private static bool ExecCommand(string cmdName, string[] cmdArgs)
        {
            MethodInfo[] props = typeof(Command).GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Public);
            bool found = false;
            foreach (MethodInfo prop in props)
            {
                foreach (object attr in prop.GetCustomAttributes(true))
                {
                    CommandAttribute cmdAttr = attr as CommandAttribute;
                    if (cmdAttr != null && cmdAttr.Name == cmdName)
                    {
                        found = true;
                        prop.Invoke(null, new object[] {
                            cmdArgs
                        });
                    }
                }
            }

            return found;
        }

        public static void Out(string what)
        {
            Console.WriteLine("\r{0}", what);
            Console.Write(commandBuffer);
        }
        #endregion
    }
}
