using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbyUDPServer : MonoBehaviour
{
    public int port = 9050;
    public string hostName = "Host";
    public bool autoStart = false;
    public float timeout = 5f;

    public GameObject createPanel;
    public GameObject roomPanel;
    public TMP_InputField nameInput;
    public TMP_InputField chatInput;
    public TMP_Text infoText;
    public TMP_Text playersText;
    public TMP_Text chatText;

    const int MaxPacketSize = 64 * 1024;
    const int MaxChatLines = 14;

    struct Packet { public byte[] data; public EndPoint from; }

    Socket m_socket;
    readonly Dictionary<EndPoint, string> m_players = new Dictionary<EndPoint, string>();
    readonly Dictionary<EndPoint, float> m_lastMessage = new Dictionary<EndPoint, float>();
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<Packet> m_inbox = new ConcurrentQueue<Packet>();
    readonly List<string> m_log = new List<string>();
    readonly List<string> m_chat = new List<string>();
    volatile bool m_running;

    public bool IsRunning { get { return m_running; } }

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
        InvokeRepeating("CheckTimeouts", 1f, 1f);
    }

    public void Disconnect()
    {
        if (!m_running) return;
        m_running = false;
        CancelInvoke();

        if (m_socket != null) { try { m_socket.Close(); } catch { } m_socket = null; }

        Thread[] threads;
        lock (m_threads) { threads = m_threads.ToArray(); m_threads.Clear(); }
        foreach (Thread t in threads) if (t != Thread.CurrentThread) t.Join(500);

        m_players.Clear();
        m_lastMessage.Clear();
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
        try { m_socket = StartServer(); }
        catch (SocketException e)
        {
            Log("[SERVER] Could not start: " + e.SocketErrorCode);
            return;
        }

        Log("[SERVER] Listening on " + port);

        ReceiveLoop();
    }

    public void SendPacket(byte[] payload, EndPoint to)
    {
        if (m_socket == null || to == null || payload == null) return;

        try { SendRaw(m_socket, payload, to); }
        catch (SocketException e) { Log("[SERVER] Send failed: " + e.SocketErrorCode); }
        catch (ObjectDisposedException) { }
    }

    public void SendString(string text, EndPoint to)
    {
        SendPacket(Messages.Text(text), to);
    }

    public void SendToAll(string text)
    {
        foreach (EndPoint e in m_players.Keys) SendString(text, e);
    }

    void ReceiveLoop()
    {
        byte[] buffer = new byte[MaxPacketSize];

        while (m_running)
        {
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);

            int received;
            try { received = ReceiveRaw(m_socket, buffer, ref from); }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.ConnectionReset) continue;
                if (m_running) Log("[SERVER] Receive failed: " + e.SocketErrorCode);
                return;
            }
            catch (ObjectDisposedException) { return; }

            if (received == 0) continue;

            byte[] payload = new byte[received];
            Array.Copy(buffer, payload, received);

            m_inbox.Enqueue(new Packet { data = payload, from = from });
        }
    }

    void StartThread(ThreadStart work)
    {
        Thread t = new Thread(work);
        t.IsBackground = true;
        lock (m_threads) m_threads.Add(t);
        t.Start();
    }

    public void Log(string message)
    {
        Debug.Log(message);
        lock (m_log) { m_log.Add(message); if (m_log.Count > 100) m_log.RemoveAt(0); }
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

        hostName = nameInput.text;
        if (hostName == "") hostName = "Host";

        infoText.text = "UDP server   port " + port + "   host " + hostName;
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

    void CheckTimeouts()
    {
        List<EndPoint> endPoints = new List<EndPoint>(m_lastMessage.Keys);
        foreach (EndPoint e in endPoints)
        {
            if (Time.time - m_lastMessage[e] > timeout)
            {
                Log("[SERVER] Timeout: " + e);
                RemovePlayer(e);
            }
        }
    }

    Socket StartServer()
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, port));
        return socket;
    }

    int SendRaw(Socket socket, byte[] data, EndPoint to)
    {
        return socket.SendTo(data, to);
    }

    int ReceiveRaw(Socket socket, byte[] buffer, ref EndPoint from)
    {
        return socket.ReceiveFrom(buffer, ref from);
    }

    // The first byte says the type: Transform is game data, Text are the lobby messages of the 2.2
    void OnPacketReceived(byte[] data, EndPoint from)
    {
        string text;
        TransformDTO dto;
        MsgType msgType;
        try { msgType = Messages.Read(data, out text, out dto); }
        catch (Exception e)
        {
            Log("[SERVER] Bad message from " + from + ": " + e.Message);
            return;
        }

        if (msgType != MsgType.Text) return;

        int separator = text.IndexOf(':');
        if (separator < 0) return;

        string type = text.Substring(0, separator);
        string content = text.Substring(separator + 1);

        if (type != "PING") Log("[SERVER] Received: " + text + " from " + from);

        if (type == "JOIN")
        {
            if (m_players.ContainsKey(from)) return;
            if (content == "") content = "Player";

            m_players.Add(from, content);
            m_lastMessage[from] = Time.time;
            Log("[SERVER] New client: " + from);
            AddChatLine(content + " joined");
            SendPlayerList();
            return;
        }

        if (!m_players.ContainsKey(from)) return;
        m_lastMessage[from] = Time.time;

        if (type == "CHAT")
        {
            string line = m_players[from] + ": " + content;
            AddChatLine(line);
            SendToAll("CHAT:" + line);
        }
        else if (type == "LEAVE")
        {
            RemovePlayer(from);
        }
    }

    void RemovePlayer(EndPoint endPoint)
    {
        if (!m_players.ContainsKey(endPoint)) return;

        AddChatLine(m_players[endPoint] + " left");
        m_players.Remove(endPoint);
        m_lastMessage.Remove(endPoint);
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
