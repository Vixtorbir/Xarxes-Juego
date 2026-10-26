using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbyUDPClient : MonoBehaviour
{
    public string serverIp = "127.0.0.1";
    public int port = 9050;
    public string userName = "Player";
    public float timeout = 5f;

    public GameObject joinPanel;
    public GameObject roomPanel;
    public TMP_InputField nameInput;
    public TMP_InputField ipInput;
    public TMP_Text statusText;
    public TMP_Text infoText;
    public TMP_Text playersText;
    public TMP_Text chatText;
    public TMP_InputField chatInput;

    const int MaxPacketSize = 64 * 1024;
    const int MaxChatLines = 14;

    Socket m_socket;
    EndPoint m_serverEndPoint;
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<byte[]> m_inbox = new ConcurrentQueue<byte[]>();
    readonly List<string> m_chat = new List<string>();
    volatile bool m_running;

    // Filled by the START message of the server
    public List<string> gameNames = new List<string>();
    public int localId;
    public NetworkGame game;
    bool m_inGame;
    float m_lastReceived;

    void Start()
    {
        Application.runInBackground = true;
        ParseCommandLine();

        nameInput.text = userName;
        ipInput.text = serverIp;
        ShowJoinPanel("");
    }

    public void StartNetwork()
    {
        if (m_running) return;
        m_running = true;
        StartThread(ClientThread);
        InvokeRepeating("SendPing", 1f, 1f);
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

        Log("[CLIENT] Stopped");
    }

    void OnDestroy() { Disconnect(); }

    void Update()
    {
        byte[] data;
        while (m_inbox.TryDequeue(out data))
            OnPacketReceived(data);

        // In the game the host sends 20 messages a second, so if nothing arrives for a while it is gone
        if (m_inGame && Time.time - m_lastReceived > timeout) QuitGame();
    }

    void ClientThread()
    {
        m_socket = StartClient();

        try { m_serverEndPoint = ServerEndPoint(); }
        catch (FormatException)
        {
            Log("[CLIENT] Invalid IP: " + serverIp);
            m_inbox.Enqueue(null);
            return;
        }

        Log("[CLIENT] Ready, server is " + m_serverEndPoint);

        OnStarted();
        ReceiveLoop();
    }

    public void SendPacket(byte[] payload)
    {
        if (m_socket == null || m_serverEndPoint == null || payload == null) return;

        try { SendRaw(m_socket, payload, m_serverEndPoint); }
        catch (SocketException e) { Log("[CLIENT] Send failed: " + e.SocketErrorCode); }
        catch (ObjectDisposedException) { }
    }

    public void SendString(string text)
    {
        SendPacket(Messages.Text(text));
    }

    public void SendTransform(TransformDTO dto)
    {
        SendPacket(Messages.Transform(dto));
    }

    public void SendShoot(ShootDTO dto)
    {
        SendPacket(Messages.Shoot(dto));
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
                if (e.SocketErrorCode == SocketError.ConnectionReset)
                    Log("[CLIENT] No server on " + m_serverEndPoint);
                else if (m_running)
                    Log("[CLIENT] Receive failed: " + e.SocketErrorCode);

                m_inbox.Enqueue(null);
                return;
            }
            catch (ObjectDisposedException) { return; }

            if (received == 0) continue;

            byte[] payload = new byte[received];
            Array.Copy(buffer, payload, received);

            m_inbox.Enqueue(payload);
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
    }

    void ParseCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-ip" && i + 1 < args.Length) serverIp = args[++i];
            else if (args[i] == "-port" && i + 1 < args.Length) int.TryParse(args[++i], out port);
            else if (args[i] == "-name" && i + 1 < args.Length) userName = args[++i];
        }
    }

    public void OnJoinButton()
    {
        if (m_running) return;

        userName = nameInput.text.Replace(",", "");
        serverIp = ipInput.text;
        if (userName == "") userName = "Player";

        infoText.text = "UDP   " + serverIp + ":" + port + "   name " + userName;
        playersText.text = "";
        m_chat.Clear();
        chatText.text = "";

        joinPanel.SetActive(false);
        roomPanel.SetActive(true);

        StartNetwork();
    }

    public void OnSendButton()
    {
        if (!m_running || chatInput.text == "") return;

        SendString("CHAT:" + chatInput.text);
        chatInput.text = "";
    }

    public void OnLeaveButton()
    {
        SendString("LEAVE:");
        Disconnect();
        ShowJoinPanel("");
    }

    public void OnBackButton()
    {
        SceneManager.LoadScene("S_Menu");
    }

    void ShowJoinPanel(string status)
    {
        joinPanel.SetActive(true);
        roomPanel.SetActive(false);
        statusText.text = status;
    }

    void AddChatLine(string line)
    {
        m_chat.Add(line);
        if (m_chat.Count > MaxChatLines) m_chat.RemoveAt(0);
        chatText.text = string.Join("\n", m_chat);
    }

    void SendPing()
    {
        SendString("PING:");
    }

    Socket StartClient()
    {
        return new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    }

    EndPoint ServerEndPoint()
    {
        return new IPEndPoint(IPAddress.Parse(serverIp), port);
    }

    void OnStarted()
    {
        SendString("JOIN:" + userName);
    }

    int SendRaw(Socket socket, byte[] data, EndPoint to)
    {
        return socket.SendTo(data, to);
    }

    int ReceiveRaw(Socket socket, byte[] buffer, ref EndPoint from)
    {
        return socket.ReceiveFrom(buffer, ref from);
    }

    void OnPacketReceived(byte[] data)
    {
        if (!m_running) return;

        if (data == null)
        {
            if (m_inGame) { QuitGame(); return; }

            Disconnect();
            ShowJoinPanel("Disconnected from " + serverIp);
            return;
        }

        m_lastReceived = Time.time;

        string text;
        TransformDTO dto;
        ShootDTO shoot;
        ScoreDTO score;
        MsgType msgType;
        try { msgType = Messages.Read(data, out text, out dto, out shoot, out score); }
        catch (Exception e)
        {
            Log("[CLIENT] Bad message: " + e.Message);
            return;
        }

        if (msgType == MsgType.Transform)
        {
            if (game != null) game.OnTransformReceived(dto);
            return;
        }
        if (msgType == MsgType.Shoot)
        {
            if (game != null) game.OnShootReceived(shoot);
            return;
        }
        if (msgType == MsgType.Score)
        {
            if (game != null) game.OnScoreReceived(score);
            return;
        }

        Log("[CLIENT] Received: " + text);

        int separator = text.IndexOf(':');
        if (separator < 0) return;

        string type = text.Substring(0, separator);
        string content = text.Substring(separator + 1);

        if (type == "PLAYERS" && !m_inGame)
        {
            playersText.text = content.Replace(",", "\n");
        }
        else if (type == "CHAT" && !m_inGame)
        {
            AddChatLine(content);
        }
        else if (type == "START")
        {
            StartGame(content);
        }
        else if (type == "LEFT")
        {
            if (game != null) game.RemovePlayer(int.Parse(content));
        }
        else if (type == "END")
        {
            QuitGame();
        }
    }

    // START:<myId>:<name0>,<name1>,... The server repeats it, so only the first one counts
    void StartGame(string content)
    {
        if (m_inGame) return;

        int separator = content.IndexOf(':');
        localId = int.Parse(content.Substring(0, separator));
        gameNames.AddRange(content.Substring(separator + 1).Split(','));
        m_inGame = true;
        Log("[CLIENT] Game started, my player is " + localId);

        // This object goes on to the game scene, so the game uses the same socket as the lobby
        DontDestroyOnLoad(gameObject);
        SceneManager.LoadScene("S_Game");
    }

    public void LeaveGame()
    {
        SendString("LEAVE:");
        QuitGame();
    }

    void QuitGame()
    {
        m_inGame = false;
        Disconnect();
        Destroy(gameObject);
        SceneManager.LoadScene("S_Menu");
    }
}
