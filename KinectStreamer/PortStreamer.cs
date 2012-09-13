using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;

using System.Threading;

using System.Collections.Concurrent;
using System.ComponentModel;

namespace KinectStreamer
{
    // State associated with a client. Passed around in asynchronous methods (Begin.../End...).
    class ClientConnectionState
    {
        // Client socket.
        public readonly Socket socket;

        // "1-semaphore". For sending data to the client.
        // Prevents us from sending more while the client is still receiving.
        // Signalled when the client is ready to receive again.
        public readonly ManualResetEvent readyToReceiveEvent = new ManualResetEvent(true);

        public ClientConnectionState(Socket socket)
        {
            this.socket = socket;
        }
    }


    class PortStreamer
    {
        private int backlog;
        IPAddress host;
        private int port;

        // "1-semaphore" for accepting new clients.
        private ManualResetEvent acceptEvent = new ManualResetEvent(false);

        // All clients we are currently sending data to.
        List<ClientConnectionState> clients = new List<ClientConnectionState>();

        // New clients signing up for receiving data as well.
        // Clients can be added here from other threads,
        // and they will be added to the main clients list from the main thread in the send loop.
        ConcurrentQueue<ClientConnectionState> newClients = new ConcurrentQueue<ClientConnectionState>();

        public PortStreamer(IPAddress host, int port, int backlog)
        {
            this.host = host;
            this.port = port;
            this.backlog = backlog;
        }

        public PortStreamer(int port, int backlog)
            : this(IPAddress.Any, port, backlog)
        {
        }

        private void log(string msg)
        {
            // This could forward to a proper logging framework.
            Console.WriteLine("PortStreamer: " + msg);
        }

        private void bw_DoWork(object sender, DoWorkEventArgs e)
        {
            StartListening();
        }

        public void StartListening()
        {
            IPEndPoint localEndPoint = new IPEndPoint(host, port);

            Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            listener.Bind(localEndPoint);
            listener.Listen(backlog);

            // Client accept loop.
            while (true)
            {
                log("Waiting for a connection...");

                // Listen for connections, but only accept one at a time synchronising on acceptEvent.
                listener.BeginAccept(new AsyncCallback(AcceptDoneCallback), listener);

                // Wait until a connection is made before continuing.
                acceptEvent.WaitOne();
                acceptEvent.Reset();
            }
        }

        private void AcceptDoneCallback(IAsyncResult ar)
        {
            Socket serverSocket = (Socket)ar.AsyncState;

            try
            {
                // Finish the accepting (might throw an exception).
                // Yields specific socket that is the connection to this client.
                Socket clientSocket = serverSocket.EndAccept(ar);

                // Client was accepted successfully, signal accept loop to continue.
                acceptEvent.Set();

                // Create the state object.
                ClientConnectionState client = new ClientConnectionState(clientSocket);

                log("Got new connection from " + client.socket.RemoteEndPoint);

                // Add client to the threadsafe list of clients signing up for getting data.
                newClients.Enqueue(client);
            }
            catch (SocketException e)
            {
                log("exception accepting a client: " + e.ToString());
            }
        }

        /* Like StartListening(), but forks off to the background. */
        public void runAsyncForever()
        {
            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += new DoWorkEventHandler(bw_DoWork);
            bw.RunWorkerAsync();
        }

        // Sends the given string do all connected clients, encoding it to bytes with default encoding.
        // Does not append a newline.
        // See send(byte[] bytes) for details.
        public void send(string data)
        {
            send(Encoding.Default.GetBytes(data));
        }

        // Sends the given bytes to all connected clients.
        public void send(byte[] bytes)
        {
            // Assemble disconnected clients here as we cannot remove them while we iterate over them.
            var toRemove = new List<ClientConnectionState>();

            int clientCountOnLoopEntry = clients.Count;
            bool clientCountChanged = false;

            foreach (ClientConnectionState client in clients)
            {
                Socket socket = client.socket;

                // Wait until the client is ready to receive.
                // TODO This currently blocks all other clients, change it to skipping the client instead.
                client.readyToReceiveEvent.WaitOne();

                // Send the data. The client might have disconnected already.
                try
                {
                    socket.BeginSend(bytes, 0, bytes.Length, SocketFlags.None, SendDoneCallback, client);
                }
                catch (SocketException)
                {
                    log("Send failed during socket.BeginSend: Client " + client.socket.RemoteEndPoint + " might have disconnected"); log("Send failed: Client " + client.socket.RemoteEndPoint + " might have disconnected. Removing client.");
                    toRemove.Add(client);
                }
            }

            clientCountChanged |= toRemove.Count > 0;

            // Remove clients to whom sending failed.
            foreach (ClientConnectionState client in toRemove)
            {
                clients.Remove(client);
            }

            // Add all clients who subscribed asynchronously in the meantime.
            {
                ClientConnectionState listenerToAdd;
                while (newClients.TryDequeue(out listenerToAdd))
                {
                    clients.Add(listenerToAdd);
                }
            }

            clientCountChanged |= clients.Count != clientCountOnLoopEntry;

            if (clientCountChanged)
            {
                log("Current client count: " + clients.Count);
            }
        }

        private void SendDoneCallback(IAsyncResult ar)
        {
            ClientConnectionState client = (ClientConnectionState)ar.AsyncState;

            // The client might have disconnected during the sending.
            try
            {
                // Finish the sending (might throw an exception).
                client.socket.EndSend(ar);

                // Mark client ready for receiving again.
                client.readyToReceiveEvent.Set();
            }
            catch (SocketException)
            {
                // Ignore this here (but handle it!).
                // The exception will also be thrown where socket.BeginSend was called.
                // If we don't catch this exception here, the program will terminate if this happens.
            }
        }
    }

}
