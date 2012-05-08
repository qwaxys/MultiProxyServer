using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace MultiProxy {
    class Program {
        // Default general values
        static int listnerPort = 25565; // The port for the proxy to listen to
        static string listenerEndPoint = "0.0.0.0"; // The endpoint to listen on. 0.0.0.0 is all available interfaces.
        static TcpListener listener;// This is what we'll use to listen for incoming traffic

        // Default minecraft values
        static Dictionary<string, string> VirtualHosts = new Dictionary<string, string>(); // A dictionary of hosts and destinations for each virtual host
        static string defaultHost = null; // The default host to use if there is no match
        static string MotD = "A Minecraft Proxy"; // We can't determine which host to use from an 0xFE request, so we use global MOTD
        static bool FetchPingEnabled = false;// If true, fetch ping values from all remote servers
        static bool showUsersInMotd = false;

        // Default webserver values
        static string webserverHost = "localhost";
        static int webserverPort = 80;

        // Default irc values
        static string ircserverHost = "localhost";
        static int ircserverPort = 6667;

        static void Main(string[] args) {
            string file = "config.xml";
            if (args.Length > 0) file = args[0];
            if (!File.Exists(file)) {
                Console.WriteLine("Config file doesn't exist, creating default.");
                File.WriteAllText(file, MultiProxy.Properties.Resources.config);
            }
            LoadConfig(file);

            listener = new TcpListener(IPAddress.Parse(listenerEndPoint), listnerPort); // Open up a listener on the specified endpoint

            // Start listening for traffic
            listener.Start();
            listener.BeginAcceptTcpClient(HandleClient, null);

            Console.WriteLine("Listening to " + listenerEndPoint + ":" + listnerPort.ToString());

            string command;
            do {
                command = Console.ReadLine();
                if (command == "reload") {
                    VirtualHosts.Clear();
                    LoadConfig(file);
                    Console.WriteLine("The config is reloaded, if however you changed port and/or endpoint then stop and restart for those changes to take effect");
                }
            } while (command != "quit"); // Exit when "quit" has been typed into the console.
            listener.Stop();
        }

        static void HandleClient(IAsyncResult result) {
            // Listen for the next connection
            try {
                listener.BeginAcceptTcpClient(HandleClient, null);
                // Retrieve the new connection
                TcpClient remoteClient = listener.EndAcceptTcpClient(result);
                // Handle the new connection
                Thread t = new Thread(new ParameterizedThreadStart(HandleClient));
                t.Start(remoteClient);
            } catch {
                Environment.Exit(0); //it works, no comment :P
            }
        }

        private static void HandleClient(object remoteClientObj) {
            TcpClient remoteClient = (TcpClient)remoteClientObj;
            // Listen for a handshake packet (or a server list ping)

            // This method is executed asyncronously, and is not blocking,
            // so this code can exist with feeling bad about it.
            // Seriously, though, don't actually use this kind of packet
            // interpreter/parser/whatever. I'm only handling two packets
            // here.

            // Doing it anyway.

            string host = defaultHost;
            string originalHost = null;
            string username = null;

            try {
                byte b = (byte)remoteClient.GetStream().ReadByte();
                switch(b){
                    case 0xFE:
                        #region minecraft server list ping
                        byte[] payload = new byte[0];
                        if (FetchPingEnabled) {
                            // This will count all the players on all the vhosts
                            int players = 0;
                            int max = 0;
                            try {
                                string motdExtended = MotD;
                                foreach (String s in VirtualHosts.Values) {
                                    TcpClient remoteServer = new TcpClient();
                                    if (s.Contains(":")) {
                                        string[] parts = s.Split(':');
                                        remoteServer.ReceiveTimeout = 10000; // Ten second timeout
                                        remoteServer.Connect(parts[0], int.Parse(parts[1]));
                                        motdExtended += " " + parts[0];
                                    } else {
                                        remoteServer.ReceiveTimeout = 10000; // Ten second timeout
                                        remoteServer.Connect(s, 25565);
                                        motdExtended += " " + s;
                                    }
                                    byte[] handshakePacket = new byte[] { 0xFE }.ToArray();
                                    remoteServer.GetStream().Write(handshakePacket, 0, handshakePacket.Length);
                                    bool hasData = false;
                                    while (!hasData) {
                                        if (remoteServer.Available != 0) {
                                            // Read any waiting data
                                            const char space = '\u0000';
                                            byte[] buffer = new byte[remoteServer.Available];
                                            remoteServer.GetStream().Read(buffer, 0, buffer.Length);
                                            String[] motd = System.Text.Encoding.ASCII.GetString(buffer, 0, buffer.Length).Split('\u003f');
                                            int serverPlayers = Convert.ToInt32(new System.Text.RegularExpressions.Regex(space.ToString()).Replace(motd[2], string.Empty));
                                            players += serverPlayers;
                                            int serverMax = Convert.ToInt32(new System.Text.RegularExpressions.Regex(space.ToString()).Replace(motd[3], string.Empty));
                                            max += serverMax;
                                            remoteServer.Close();
                                            hasData = true;
                                            motdExtended += "(" + serverPlayers + "/" + serverMax + ")";
                                        }
                                        Thread.Sleep(5);
                                    }
                                }
                                if (!showUsersInMotd) motdExtended = MotD;
                                payload = new byte[] { 0xFF }.Concat(MakeString(motdExtended + "§" + players.ToString() + "§" + max.ToString())).ToArray(); // Construct a packet to respond with
                            } catch (Exception ex) {
                                Console.WriteLine(ex.Message + ex.StackTrace);
                            }
                        } else {
                            payload = new byte[] { 0xFF }.Concat(MakeString(MotD)).ToArray();
                        }
                        remoteClient.GetStream().Write(payload, 0, payload.Length);
                        remoteClient.Close();
                        return;
                        #endregion
                    case 0x02:
                        #region minecraft Handshake
                        // Retrieve the username and hostname, and parse it
                        string userAndHost = ReadString(remoteClient.GetStream());
                        string[] partsHandshake = userAndHost.Split(';');

                        if (partsHandshake.Length == 2)
                            host = partsHandshake[1];
                        if (!host.Contains(':'))
                            host += ":" + listnerPort;

                        originalHost = host;

                        if (VirtualHosts.ContainsKey(host.ToLower()))
                            host = VirtualHosts[host.ToLower()];
                        else
                            host = defaultHost;

                        username = partsHandshake[0];

                        // At this point, username should be the username, and host should be the destination host to connect to

                        if (host == null) {
                            // If there is no host, then disconnect the user.
                            if (partsHandshake.Length == 2)
                                Console.WriteLine(username + " tried to log in to " + partsHandshake[1] + ", and cannot be redirected.");
                            else
                                Console.WriteLine(username + " tried to log in and cannot be redirected.");
                            // Disconnect user
                            byte[] payloadHandshake = new byte[] { 0xFF }.Concat(MakeString("[Proxy Error]: Unable to redirect to destination.")).ToArray();
                            remoteClient.GetStream().Write(payloadHandshake, 0, payloadHandshake.Length);
                            remoteClient.Close();
                            return;
                        }
                        break;
                        #endregion
                    case 0x47: // G (http: GET)
                    case 0x48: // H (http: HEAD)
                    case 0x50: // P (http: POST and PUT)
                    case 0x44: // D (http: DELETE)
                    case 0x4F: // O (http: OPTIONS)
                    case 0x16: // SYN (https)
                        #region webserver
                        bool https = (b == 0x16);
                        TcpClient webserver = new TcpClient();
                        try {
                            webserver.ReceiveTimeout = 10000;
                            webserver.Connect(webserverHost, https ? 443 : webserverPort);

                            // Sending the initial request
                            byte[] buffer = new byte[remoteClient.Available + 1];
                            buffer[0] = b;
                            remoteClient.GetStream().Read(buffer, 1, buffer.Length - 1);
                            webserver.GetStream().Write(buffer, 0, buffer.Length);

                            // comments: see same code later
                            while (webserver.Connected && remoteClient.Connected) {
                                if (webserver.Available != 0) {
                                    buffer = new byte[webserver.Available];
                                    webserver.GetStream().Read(buffer, 0, buffer.Length);
                                    remoteClient.GetStream().Write(buffer, 0, buffer.Length);
                                }
                                if (remoteClient.Available != 0) {
                                    buffer = new byte[remoteClient.Available];
                                    remoteClient.GetStream().Read(buffer, 0, buffer.Length);
                                    webserver.GetStream().Write(buffer, 0, buffer.Length);
                                }
                                Thread.Sleep(1);
                            }
                            webserver.Close();
                        } catch {
                            remoteClient.Close();
                            webserver.Close();
                        }
                        remoteClient.Close();
                        return;
                        #endregion
                    case 0x4E: // N (irc: NICK) 
                        #region ircserver
                        TcpClient ircserver = new TcpClient();
                        try {
                            ircserver.ReceiveTimeout = 10000;
                            ircserver.Connect(ircserverHost, ircserverPort);

                            // Sending the initial request
                            byte[] buffer = new byte[remoteClient.Available + 1];
                            buffer[0] = b;
                            remoteClient.GetStream().Read(buffer, 1, buffer.Length - 1);
                            ircserver.GetStream().Write(buffer, 0, buffer.Length);

                            // comments: see same code later
                            while (ircserver.Connected && remoteClient.Connected) {
                                if (ircserver.Available != 0) {
                                    buffer = new byte[ircserver.Available];
                                    ircserver.GetStream().Read(buffer, 0, buffer.Length);
                                    remoteClient.GetStream().Write(buffer, 0, buffer.Length);
                                }
                                if (remoteClient.Available != 0) {
                                    buffer = new byte[remoteClient.Available];
                                    remoteClient.GetStream().Read(buffer, 0, buffer.Length);
                                    ircserver.GetStream().Write(buffer, 0, buffer.Length);
                                }
                                Thread.Sleep(1);
                            }
                            ircserver.Close();

                        } catch {
                            // Disconnect
                            remoteClient.Close();
                            ircserver.Close();
                        }

                        remoteClient.Close();
                        return;
                        #endregion
                    default:
                        remoteClient.Close();
                        return;
                }
            } catch { }

            #region minecraft virtual servers
            // If we got this far, we should be able to connect the user properly.
            try {
                Console.WriteLine(username + " logged in to " + originalHost + ", redirecting to " + host);

                // Connect to the requested server
                TcpClient remoteServer = new TcpClient();
                string[] parts = host.Split(':');
                remoteServer.ReceiveTimeout = 10000; // Ten second timeout
                remoteServer.Connect(parts[0], int.Parse(parts[1]));

                // Create and send a handshake packet
                byte[] handshakePacket = new byte[] { 0x02 }.Concat(MakeString(username + ";" + host)).ToArray();
                remoteServer.GetStream().Write(handshakePacket, 0, handshakePacket.Length);
                // Get the two talking
                while (remoteServer.Connected && remoteClient.Connected) {
                    // Read from the server
                    if (remoteServer.Available != 0) {
                        // Read any waiting data
                        byte[] buffer = new byte[remoteServer.Available];
                        remoteServer.GetStream().Read(buffer, 0, buffer.Length);
                        // And write it back to the client.
                        remoteClient.GetStream().Write(buffer, 0, buffer.Length);
                    }
                    // Read from the client
                    if (remoteClient.Available != 0) {
                        // Read any waiting data
                        byte[] buffer = new byte[remoteClient.Available];
                        remoteClient.GetStream().Read(buffer, 0, buffer.Length);
                        // And write it back to the server.
                        remoteServer.GetStream().Write(buffer, 0, buffer.Length);
                    }
                    Thread.Sleep(1); // If you don't do this, you will ruin your processor
                }
            } catch {
                try {
                    // Disconnect the user.
                    byte[] payload = new byte[] { 0xFF }.Concat(MakeString("[Proxy Error]: Unable to connect to remote server.")).ToArray();
                    remoteClient.GetStream().Write(payload, 0, payload.Length);
                    remoteClient.Close();
                } catch { }
            }
            #endregion
        }

        private static void LoadConfig(string file) {
            StreamReader reader = new StreamReader(File.Open(file, FileMode.Open));
            XDocument document = XDocument.Parse(reader.ReadToEnd());
            reader.Close();

            //webserver config
            if (document.Root.Element("webserver") != null) {
                webserverHost = document.Root.Element("webserver").Value;
                if (webserverHost.Contains(':')) {
                    string[] parts = webserverHost.Split(':');
                    webserverHost = parts[0];
                    webserverPort = int.Parse(parts[1]);
                }
            }

            //ircserver config
            if (document.Root.Element("ircserver") != null) {
                ircserverHost = document.Root.Element("ircserver").Value;
                if (ircserverHost.Contains(':')) {
                    string[] parts = ircserverHost.Split(':');
                    ircserverHost = parts[0];
                    ircserverPort = int.Parse(parts[1]);
                }
            }

            //minecraft config
            if (document.Root.Element("port") != null)
                listnerPort = int.Parse(document.Root.Element("port").Value);
            if (document.Root.Element("endpoint") != null)
                listenerEndPoint = document.Root.Element("endpoint").Value;
            if (document.Root.Element("motd") != null)
                MotD = document.Root.Element("motd").Value;
            if (document.Root.Element("usersinmotd") != null)
                showUsersInMotd = bool.Parse(document.Root.Element("usersinmotd").Value);
            if (document.Root.Element("pingremote") != null)
                FetchPingEnabled = bool.Parse(document.Root.Element("pingremote").Value);

            if (document.Root.Element("vhosts") == null) {
                // We should only attempt to execute if there are defined virutal hosts
                Console.WriteLine("[Error]: No virtual hosts defined in the config file!");
                return;
            }

            // Iterate through each defined virtual host
            foreach (XElement element in document.Root.Element("vhosts").Elements("vhost")) {
                // Validate the XML element
                if (element.Attribute("host") == null || element.Attribute("destination") == null) {
                    Console.WriteLine("[Error]: Config file has invalid hosts");
                    return;
                }

                // Parse this element into a virtual host
                string host = element.Attribute("host").Value;
                // Ensure it has a port
                if (!host.Contains(':'))
                    host += ":" + listnerPort;
                VirtualHosts.Add(host, element.Attribute("destination").Value);
            }

            // Parse the default host from XML
            if (document.Root.Element("vhosts").Element("default") == null)
                Console.WriteLine("[Warning]: No default host specified.");
            else {
                XElement defaultVHost = document.Root.Element("vhosts").Element("default");
                // Validate it
                if (defaultVHost.Attribute("destination") == null) {
                    Console.WriteLine("[Error]: Default host is invalid");
                    return;
                }

                // Parse it
                defaultHost = defaultVHost.Attribute("destination").Value;
                if (!defaultHost.Contains(':'))
                    defaultHost += ":25565";
            }
        }

        // Code stolen from my other projects follows. (written by SirCmpwn)
        public static byte[] MakeString(String msg) {
            short len = IPAddress.HostToNetworkOrder((short)msg.Length);
            byte[] a = BitConverter.GetBytes(len);
            byte[] b = Encoding.BigEndianUnicode.GetBytes(msg);
            return a.Concat(b).ToArray();
        }

        public static string ReadString(Stream s) {
            byte[] lengthArray = new byte[sizeof(short)];
            s.Read(lengthArray, 0, sizeof(short));

            short length = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(lengthArray, 0));

            byte[] stringArray = new byte[length * 2];
            s.Read(stringArray, 0, length * 2);

            return Encoding.BigEndianUnicode.GetString(stringArray);
        }
    }
}