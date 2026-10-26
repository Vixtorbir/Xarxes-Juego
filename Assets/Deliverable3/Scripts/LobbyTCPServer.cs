using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbyTCPServer : MonoBehaviour
{
    public int port = 9050;
    public string hostName = "Host";
    public bool autoStart = false;

    public GameObject createPanel;
    public GameObject roomPanel;
    public TMP_InputField nameInput;
    public TMP_InputField chatInput;
    public TMP_Text infoText;
    public TMP_Text playersText;
    public TMP_Text chatText;

    const int MaxPacketSize = 64 * 1024;
    const int MaxChatLines = 14;

    struct Packet { public byte[] data; public Socket from; }

    Socket m_listener;
    readonly List<Socket> m_clients = new List<Socket>();
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<Packet> m_inbox = new ConcurrentQueue<Packet>();
    readonly Dictionary<Socket, string> m_players = new Dictionary<Socket, string>();
    readonly List<string> m_chat = new List<string>();
    volatile bool m_running;

    void Start()
    {
        Application.runInBackground = true;
        ParseCommandLine();

        nameInput.text = hostName;
        createPanel.SetActive(true);
        roomPanel.SetActive(false);

        if (autoStart) OnCreateButton();
    }

    public void StartNetwork()
    {
        if (m_running) return;
        m_running = true;
        StartThread(ServerThread);
    }

    public void Disconnect()
    {
        if (!m_running) return;
        m_running = false;

        Socket[] clients;
        lock (m_clients) { clients = m_clients.ToArray(); m_clients.Clear(); }
        foreach (Socket c in clients) CloseSocket(c);

        CloseSocket(m_listener); m_listener = null;

        Thread[] threads;
        lock (m_threads) { threads = m_threads.ToArray(); m_threads.Clear(); }
        foreach (Thread t in threads) if (t != Thread.CurrentThread) t.Join(500);

        m_players.Clear();
        Log("[SERVER] Stopped");
    }

    void OnDestroy() { Disconnect(); }

    void Update()
    {
        Packet packet;
        while (m_inbox.TryDequeue(out packet))
            OnPacketReceived(packet.data, packet.from);
    }

    void ServerThread()
    {
        try { m_listener = StartServer(); }
        catch (SocketException e)
        {
            Log("[SERVER] Could not start: " + e.SocketErrorCode);
            return;
        }

        Log("[SERVER] Listening on " + port);

        while (m_running)
        {
            Socket client;
            try { client = AcceptClient(); }
            catch (SocketException) { break; }
            catch (ObjectDisposedException) { break; }

            lock (m_clients) m_clients.Add(client);
            Log("[SERVER] Client connected: " + client.RemoteEndPoint);

            Socket captured = client;
            StartThread(delegate { ClientThread(captured); });
        }
    }

    void ClientThread(Socket client)
    {
        ReceiveLoop(client);
        lock (m_clients) m_clients.Remove(client);
        Log("[SERVER] Client disconnected");
        CloseSocket(client);

        m_inbox.Enqueue(new Packet { data = Encoding.UTF8.GetBytes("LEAVE:"), from = client });
    }

    public void SendPacket(byte[] payload, Socket to)
    {
        if (to == null || payload == null) return;

        byte[] framed = new byte[4 + payload.Length];
        BitConverter.GetBytes(payload.Length).CopyTo(framed, 0);
        payload.CopyTo(framed, 4);

        try { SendRaw(to, framed); }
        catch (SocketException e) { Log("[SERVER] Send failed: " + e.SocketErrorCode); }
        catch (ObjectDisposedException) { }
    }

    public void SendString(string text, Socket to)
    {
        SendPacket(Encoding.UTF8.GetBytes(text), to);
    }

    public void SendToAll(string text)
    {
        Socket[] targets;
        lock (m_clients) targets = m_clients.ToArray();
        foreach (Socket c in targets) SendString(text, c);
    }

    void ReceiveLoop(Socket socket)
    {
        byte[] header = new byte[4];
        while (m_running)
        {
            if (!ReadExactly(socket, header, 4)) return;

            int size = BitConverter.ToInt32(header, 0);
            if (size <= 0 || size > MaxPacketSize) { Log("[SERVER] Invalid packet size: " + size); return; }

            byte[] payload = new byte[size];
            if (!ReadExactly(socket, payload, size)) return;

            m_inbox.Enqueue(new Packet { data = payload, from = socket });
        }
    }

    bool ReadExactly(Socket socket, byte[] buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read;
            try { read = ReceiveRaw(socket, buffer, total, count - total); }
            catch (SocketException e) { Log("[SERVER] Connection lost: " + e.SocketErrorCode); return false; }
            catch (ObjectDisposedException) { return false; }

            if (read == 0) return false;
            total += read;
        }
        return true;
    }

    void StartThread(ThreadStart work)
    {
        Thread t = new Thread(work);
        t.IsBackground = true;
        lock (m_threads) m_threads.Add(t);
        t.Start();
    }

    void CloseSocket(Socket socket)
    {
        if (socket == null) return;
        try { socket.Shutdown(SocketShutdown.Both); } catch { }
        try { socket.Close(); } catch { }
    }

    public void Log(string message)
    {
        Debug.Log(message);
    }

    void ParseCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-port" && i + 1 < args.Length) int.TryParse(args[++i], out port);
            else if (args[i] == "-name" && i + 1 < args.Length) hostName = args[++i];
            else if (args[i] == "-autostart") autoStart = true;
        }
    }

    public void OnCreateButton()
    {
        if (m_running) return;

        hostName = nameInput.text.Replace(",", "");
        if (hostName == "") hostName = "Host";

        infoText.text = "TCP server   port " + port + "   host " + hostName;
        m_chat.Clear();
        chatText.text = "";
        UpdatePlayersText();

        createPanel.SetActive(false);
        roomPanel.SetActive(true);

        StartNetwork();
    }

    public void OnSendButton()
    {
        if (!m_running || chatInput.text == "") return;

        string line = hostName + ": " + chatInput.text;
        AddChatLine(line);
        SendToAll("CHAT:" + line);
        chatInput.text = "";
    }

    public void OnBackButton()
    {
        SceneManager.LoadScene("S_Menu");
    }

    public void OnCloseButton()
    {
        Disconnect();
        SceneManager.LoadScene("S_Menu");
    }

    void UpdatePlayersText()
    {
        playersText.text = hostName + " (host)\n";
        foreach (string name in m_players.Values)
            playersText.text += name + "\n";
    }

    void AddChatLine(string line)
    {
        m_chat.Add(line);
        if (m_chat.Count > MaxChatLines) m_chat.RemoveAt(0);
        chatText.text = string.Join("\n", m_chat);
    }

    Socket StartServer()
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Any, port));
        socket.Listen(10);
        return socket;
    }

    Socket AcceptClient()
    {
        return m_listener.Accept();
    }

    int SendRaw(Socket socket, byte[] data)
    {
        return socket.Send(data);
    }

    int ReceiveRaw(Socket socket, byte[] buffer, int offset, int count)
    {
        return socket.Receive(buffer, offset, count, SocketFlags.None);
    }

    void OnPacketReceived(byte[] data, Socket from)
    {
        string text = Encoding.UTF8.GetString(data);
        Log("[SERVER] Received: " + text);

        int separator = text.IndexOf(':');
        if (separator < 0) return;

        string type = text.Substring(0, separator);
        string content = text.Substring(separator + 1);

        if (type == "JOIN")
        {
            if (m_players.ContainsKey(from)) return;

            // PLAYERS separates the names with commas
            content = content.Replace(",", "");
            if (content == "") content = "Player";

            m_players.Add(from, content);
            AddChatLine(content + " joined");
            SendPlayerList();
        }
        else if (type == "CHAT")
        {
            if (!m_players.ContainsKey(from)) return;

            string line = m_players[from] + ": " + content;
            AddChatLine(line);
            SendToAll("CHAT:" + line);
        }
        else if (type == "LEAVE")
        {
            RemovePlayer(from);
        }
    }

    void RemovePlayer(Socket socket)
    {
        if (!m_players.ContainsKey(socket)) return;

        AddChatLine(m_players[socket] + " left");
        m_players.Remove(socket);
        SendPlayerList();
    }

    void SendPlayerList()
    {
        string list = hostName;
        foreach (string name in m_players.Values) list += "," + name;

        SendToAll("PLAYERS:" + list);
        UpdatePlayersText();
    }
}
