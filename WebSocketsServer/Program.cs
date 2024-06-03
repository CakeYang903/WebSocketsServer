using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace WebSocketsServer
{
    public class ConnectedClientInfo
    {
        public WebSocket WebSocket { get; set; }
        public string GroupID { get; set; }
        public string RemoteEndPoint { get; set; }
        public string ClientID { get; set; }
    }

    class Program
    {
        private static Dictionary<string, ConnectedClientInfo> connectedGroups = new Dictionary<string, ConnectedClientInfo>();
        private static HashSet<ConnectedClientInfo> connectedClientsCache = new HashSet<ConnectedClientInfo>();
        private static CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        static async Task Main()
        {
            string listenerStr = "http://127.0.0.1:8080/";
            var listener = new HttpListener();
            listener.Prefixes.Add(listenerStr);
            listener.Start();

            Console.WriteLine("Server started. Listening for connections on " + listenerStr);

            // 開啟背景執行緒處理非同步任務
            Task.Run(() => PeriodicCheckAsync(cancellationTokenSource.Token));
            Task.Run(() => HandleConsoleInputAsync(cancellationTokenSource.Token));

            while (true)
            {
                var context = await listener.GetContextAsync();
                _ = ProcessWebSocketRequestAsync(context);
            }
        }

        private static async Task PeriodicCheckAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var currentConnectedClients = connectedGroups.Values.ToList();

                // Check for new or removed clients
                var newClients = currentConnectedClients.Except(connectedClientsCache).ToList();
                var removedClients = connectedClientsCache.Except(currentConnectedClients).ToList();

                if (newClients.Any() || removedClients.Any())
                {
                    Console.WriteLine("Connected Clients:");
                    foreach (var client in currentConnectedClients)
                    {
                        Console.WriteLine($"GroupID: {client.GroupID}, RemoteEndPoint: {client.RemoteEndPoint}, ClientID: {client.ClientID}");
                    }
                }

                // Update the cache with the current connected clients
                connectedClientsCache = new HashSet<ConnectedClientInfo>(currentConnectedClients);

                await Task.Delay(1000); // 每秒檢查一次
            }
        }

        private static void ShowConnectedClients()
        {
            Console.WriteLine("Connected Clients:");

            foreach (var clientKey in connectedGroups.Keys)
            {
                Console.WriteLine(clientKey);
            }
        }

        private static async Task HandleConsoleInputAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Console.Write("Enter 'list' to show connected clients, 'exit' to close, or 'send' to send a message to a client: ");
                string input = Console.ReadLine();

                switch (input.ToLower())
                {
                    case "send":
                        Console.Write("Enter target (IP:Port, group ID, or 'all'): ");
                        string target = Console.ReadLine();

                        Console.Write("Enter message: ");
                        string message = Console.ReadLine();

                        await SendMessageAsync(target, message);
                        break;

                    case "list":
                        ShowConnectedClientsAndGroups();
                        break;

                    default:
                        Console.WriteLine("Invalid command. Please enter 'list', 'exit', or 'send'.");
                        break;
                }
            }
        }

        private static async Task SendMessageAsync(string target, string message)
        {
            if (target.Contains(":") && IPAddress.TryParse(target.Split(':')[0], out IPAddress ipAddress) && int.TryParse(target.Split(':')[1], out int port))
            {
                // 如果目標是 ip:port，則尋找對應的 HostAddress
                string clientKey = $"{ipAddress}:{port}";
                var matchingClient = connectedGroups
                    .FirstOrDefault(kv => kv.Value.RemoteEndPoint == clientKey)
                    .Value.WebSocket;
                if (matchingClient != null && matchingClient.State == WebSocketState.Open)
                {
                    var buffer = Encoding.UTF8.GetBytes(message);
                    await matchingClient.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                }
                else
                {
                    Console.WriteLine($"Client with HostAddress {clientKey} not found.");
                }
            }
            else if (target.ToLower() == "all")
            {
                await BroadcastMessageAsync(message);
            }
            else
            {
                Console.WriteLine("使用groupID發送");
                // 如果目標是 groupID，則尋找對應的 groupID
                string groupID = target;
                var matchingClients = connectedGroups
                    .Where(kv => kv.Value.GroupID == groupID)
                    .Select(kv => kv.Value)
                    .ToList();



                if (matchingClients.Any())
                {
                    foreach (var matchingClient in matchingClients)
                    {
                        Console.WriteLine($"目標對象有: {matchingClient.RemoteEndPoint} , GroupID:{matchingClient.GroupID}");
                        if (matchingClient.WebSocket.State == WebSocketState.Open)
                        {
                            var buffer = Encoding.UTF8.GetBytes(message);
                            await matchingClient.WebSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"No clients found for GroupID {groupID}.");
                }
            }


        }

        private static void ShowConnectedClientsAndGroups()
        {
            Console.WriteLine("Connected Clients and Groups:");
            foreach (var entry in connectedGroups)
            {
                Console.WriteLine($"{entry.Key}");
            }
        }



        private static async Task ProcessWebSocketRequestAsync(HttpListenerContext context)
        {
            var listenerWebSocketContext = await context.AcceptWebSocketAsync(subProtocol: null);
            var webSocket = listenerWebSocketContext.WebSocket;
            Console.WriteLine();
            Console.WriteLine($"WebSocket connection established with client RemoteEndPoint: {context.Request.RemoteEndPoint}");
            Console.WriteLine($"WebSocket connection established with client Headers: {context.Request.Headers}");
            Console.WriteLine($"WebSocket connection established with client UserAgent: {context.Request.UserAgent}");
            Console.WriteLine($"WebSocket connection established with client UserHostName: {context.Request.UserHostName}");
            Console.WriteLine($"WebSocket connection established with client UserHostAddress: {context.Request.UserHostAddress}");
            Console.WriteLine($"WebSocket connection established with client ContentType: {context.Request.ContentType}");

            // Add the connected client to a group
            string groupID = context.Request.Headers["ClientGroup"];
            string remoteEndPoint = context.Request.RemoteEndPoint.ToString();
            string clientID = context.Request.Headers["ClientID"];
            var data = new { clientID, groupID, remoteEndPoint };
            string dataJsonString = JsonSerializer.Serialize(data);

            var connectedClientInfo = new ConnectedClientInfo
            {
                WebSocket = webSocket,
                GroupID = groupID,
                RemoteEndPoint = remoteEndPoint,
                ClientID = clientID
            };
            connectedGroups.TryAdd(dataJsonString, connectedClientInfo);

            var buffer = new byte[1024];

            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Console.WriteLine($"Received message from {context.Request.RemoteEndPoint}: {message}");

                        // 檢查是否為 "ping"，若是則回傳 "pong"
                        if (message.ToLower() == "ping")
                        {
                            var responseBytes = Encoding.UTF8.GetBytes("pong");
                            await webSocket.SendAsync(new ArraySegment<byte>(responseBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                            Console.WriteLine($"Sent 'pong' to {context.Request.RemoteEndPoint}");
                        }
                    }
                }
            }
            finally
            {
                // Remove the client from the dictionary when the connection is closed
                //connectedGroups.TryRemove(context.Request.RemoteEndPoint.ToString(), out _);

                // 顯示伺服器與客戶端斷開連線的訊息   
                Console.WriteLine();
                Console.WriteLine($"WebSocket connection closed with client: {context.Request.RemoteEndPoint}");
            }
        }

        private static async Task BroadcastMessageAsync(string message)
        {
            var startSendTime = DateTime.Now;
            int connectedClientCount = 0;
            // 遍歷所有群組
            foreach (var groupMembers in connectedGroups.Values)
            {
                var buffer = Encoding.UTF8.GetBytes(message);
                await groupMembers.WebSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                connectedClientCount += 1;
                //發送訊息給所有client
            }
            Console.WriteLine($"Send {connectedClientCount} Client Message time : {(DateTime.Now - startSendTime).Milliseconds} Milliseconds.");
        }

    }
}