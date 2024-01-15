using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketsServer
{
    class Program
    {
        private static ConcurrentDictionary<string, WebSocket> connectedClients = new ConcurrentDictionary<string, WebSocket>();
        private static HashSet<string> previousClientKeys = new HashSet<string>();
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
                HashSet<string> currentClientKeys = new HashSet<string>(connectedClients.Keys);

                // 如果清單有變化，顯示連線清單
                if (!currentClientKeys.SetEquals(previousClientKeys))
                {
                    Console.WriteLine("Connected Clients:");
                    foreach (var clientKey in currentClientKeys)
                    {
                        Console.WriteLine(clientKey);
                    }

                    previousClientKeys = currentClientKeys;
                }

                // 等待1秒
                await Task.Delay(1000);

                // 如果需要在每次檢查之後執行其他任務，可以在這裡添加相應的程式碼。
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
                    case "list":
                        ShowConnectedClients();
                        break;

                    case "exit":
                        cancellationTokenSource.Cancel();
                        break;

                    case "send":
                        Console.Write("Enter client key (IP:Port) to send a message: ");
                        string clientKey = Console.ReadLine();

                        Console.Write("Enter message: ");
                        string message = Console.ReadLine();

                        await SendMessageToClientAsync(clientKey, message);
                        break;

                    case "all":
                        Console.Write("Enter message To All: ");
                        string messageAll = Console.ReadLine();

                        // 廣播接收到的訊息給所有客戶端
                        await BroadcastMessageAsync($"{messageAll}");
                        break;

                    default:
                        Console.WriteLine("Invalid command. Please enter 'list', 'exit', or 'send'.");
                        break;
                }
            }
        }

        private static async Task SendMessageToClientAsync(string clientKey, string message)
        {
            if (connectedClients.TryGetValue(clientKey, out var webSocket))
            {
                var buffer = Encoding.UTF8.GetBytes(message);
                await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            else
            {
                Console.WriteLine($"Client {clientKey} not found.");
            }
        }

        private static void ShowConnectedClients()
        {
            Console.WriteLine("Connected Clients:");
            foreach (var clientKey in connectedClients.Keys)
            {
                Console.WriteLine(clientKey);
            }
        }

        private static async Task ProcessWebSocketRequestAsync(HttpListenerContext context)
        {
            var listenerWebSocketContext = await context.AcceptWebSocketAsync(subProtocol: null);
            var webSocket = listenerWebSocketContext.WebSocket;
            Console.WriteLine();
            Console.WriteLine($"WebSocket connection established with client: {context.Request.RemoteEndPoint}");

            // Add the connected client to the dictionary
            connectedClients.TryAdd(context.Request.RemoteEndPoint.ToString(), webSocket);

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

                        
                    }
                }
            }
            finally
            {
                // Remove the client from the dictionary when the connection is closed
                connectedClients.TryRemove(context.Request.RemoteEndPoint.ToString(), out _);

                // 顯示伺服器與客戶端斷開連線的訊息   
                Console.WriteLine();
                Console.WriteLine($"WebSocket connection closed with client: {context.Request.RemoteEndPoint}");
            }
        }

        private static async Task BroadcastMessageAsync(string message)
        {
            foreach (var client in connectedClients.Values)
            {
                if (client.State == WebSocketState.Open)
                {
                    var buffer = Encoding.UTF8.GetBytes(message);
                    await client.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
        }

    }

}