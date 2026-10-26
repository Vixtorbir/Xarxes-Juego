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

public class LobbyTCPClient : MonoBehaviour
{
    public string serverIp = "127.0.0.1";
    public int port = 9050;
    public string userName = "Player";

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

    Socket m_connection;
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<byte[]> m_inbox = new ConcurrentQueue<byte[]>();
    readonly List<string> m_log = new List<string>();
    readonly List<string> m_chat = new List<string>();
    volatile bool m_running;

    public bool IsRunning { get { return m_running; } }

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
    }

    public void Disconnect()
    {
        if (!m_running) return;
        m_running = false;

        CloseSocket(m_connection); m_connection = null;

        Thread[] threads;
        lock (m_threads) { threads = m_threads.ToArray(); m_threads.Clear(); }
        foreach (Thread t in threads) if (t != Thread.CurrentThread) t.Join(500);

        Log("[CLIENT] Disconnected");
    }

    void OnDestroy() { Disconnect(); }

    void Update()
    {
        byte[] data;
        while (m_inbox.TryDequeue(out data))
            OnPacketReceived(data);
    }

    void ClientThread()
    {
        try { m_connection = StartClient(); }
        catch (SocketException e)
        {
            Log("[CLIENT] Could not connect: " + e.SocketErrorCode);
            m_inbox.Enqueue(null);
            return;
        }
        catch (FormatException)
        {
            Log("[CLIENT] Invalid IP: " + serverIp);
            m_inbox.Enqueue(null);
            return;
        }

        Log("[CLIENT] Connected to " + serverIp + ":" + port);

        OnConnected();
        ReceiveLoop(m_connection);
        Log("[CLIENT] Connection closed");

        m_inbox.Enqueue(null);
    }

    public void SendPacket(byte[] payload)
    {
        if (m_connection == null || payload == null) return;

        byte[] framed = new byte[4 + payload.Length];
        BitConverter.GetBytes(payload.Length).CopyTo(framed, 0);
        payload.CopyTo(framed, 4);

        try { SendRaw(m_connection, framed); }
        catch (SocketException e) { Log("[CLIENT] Send failed: " + e.SocketErrorCode); }
        catch (ObjectDisposedException) { }
    }

    public void SendString(string text)
    {
        SendPacket(Encoding.UTF8.GetBytes(text));
    }

    void ReceiveLoop(Socket socket)
    {
        byte[] header = new byte[4];
        while (m_running)
        {
            if (!ReadExactly(socket, header, 4)) return;

            int size = BitConverter.ToInt32(header, 0);
            if (size <= 0 || size > MaxPacketSize) { Log("[CLIENT] Invalid packet size: " + size); return; }

            byte[] payload = new byte[size];
            if (!ReadExactly(socket, payload, size)) return;

            m_inbox.Enqueue(payload);
        }
    }

    bool ReadExactly(Socket socket, byte[] buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read;
            try { read = ReceiveRaw(socket, buffer, total, count - total); }
            catch (SocketException e) { Log("[CLIENT] Connection lost: " + e.SocketErrorCode); return false; }
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
        lock (m_log) { m_log.Add(message); if (m_log.Count > 100) m_log.RemoveAt(0); }
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

        userName = nameInput.text;
        serverIp = ipInput.text;
        if (userName == "") userName = "Player";

        infoText.text = "TCP   " + serverIp + ":" + port + "   name " + userName;
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

    Socket StartClient()
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Connect(new IPEndPoint(IPAddress.Parse(serverIp), port));
        return socket;
    }

    void OnConnected()
    {
        SendString("JOIN:" + userName);
    }

    int SendRaw(Socket socket, byte[] data)
    {
        return socket.Send(data);
    }

    int ReceiveRaw(Socket socket, byte[] buffer, int offset, int count)
    {
        return socket.Receive(buffer, offset, count, SocketFlags.None);
    }

    void OnPacketReceived(byte[] data)
    {
        if (data == null)
        {
            if (!m_running) return;

            Disconnect();
            ShowJoinPanel("Disconnected from " + serverIp);
            return;
        }

        string text = Encoding.UTF8.GetString(data);
        Log("[CLIENT] Received: " + text);

        int separator = text.IndexOf(':');
        if (separator < 0) return;

        string type = text.Substring(0, separator);
        string content = text.Substring(separator + 1);

        if (type == "PLAYERS")
        {
            playersText.text = content.Replace(",", "\n");
        }
        else if (type == "CHAT")
        {
            AddChatLine(content);
        }
    }
}
