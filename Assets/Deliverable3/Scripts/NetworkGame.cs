using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Each PC moves its own player and the client sends its state (position and rotation) to the host.
// The host just copies the last state that arrived for each player.
public class NetworkGame : MonoBehaviour
{
    public float moveSpeed = 6f;
    public float jumpSpeed = 7f;
    public float gravity = 20f;
    public float arenaSize = 9f;
    public float sendInterval = 0.05f;   // 20 messages a second, not every frame

    public GameObject playerPrefab;

    const float GroundY = 1f;   // the capsule is 2 units tall and its pivot is in the middle

    LobbyUDPServer m_server;
    LobbyUDPClient m_client;
    List<string> m_names;
    int m_localId;

    readonly Dictionary<int, Transform> m_players = new Dictionary<int, Transform>();
    float m_verticalSpeed;
    float m_sendTimer;

    void Start()
    {
        m_server = FindAnyObjectByType<LobbyUDPServer>();
        m_client = FindAnyObjectByType<LobbyUDPClient>();

        if (m_server != null)
        {
            m_server.game = this;
            m_names = m_server.gameNames;
            m_localId = 0;
        }
        else if (m_client != null)
        {
            m_client.game = this;
            m_names = m_client.gameNames;
            m_localId = m_client.localId;
        }
        else
        {
            SceneManager.LoadScene("S_Menu");
            return;
        }

        for (int id = 0; id < m_names.Count; id++) SpawnPlayer(id);
    }

    // One next to the other, along the x axis
    Vector3 SpawnPosition(int id)
    {
        return new Vector3(id * 2f - 3f, GroundY, 0f);
    }

    void SpawnPlayer(int id)
    {
        GameObject player = Instantiate(playerPrefab, SpawnPosition(id), Quaternion.identity);
        player.name = "Player " + id + " " + m_names[id];
        m_players[id] = player.transform;
    }

    void Update()
    {
        if (m_names == null) return;

        if (m_players.ContainsKey(m_localId)) MoveLocalPlayer();

        m_sendTimer += Time.deltaTime;
        if (m_sendTimer >= sendInterval)
        {
            m_sendTimer = 0f;
            SendLocalPlayer();
        }
    }

    void MoveLocalPlayer()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        Transform player = m_players[m_localId];

        Vector3 direction = Vector3.zero;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) direction.z += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) direction.z -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) direction.x += 1f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) direction.x -= 1f;

        if (direction != Vector3.zero)
        {
            direction.Normalize();
            player.rotation = Quaternion.LookRotation(direction);
        }

        if (player.position.y <= GroundY)
        {
            m_verticalSpeed = 0f;
            if (keyboard.spaceKey.wasPressedThisFrame) m_verticalSpeed = jumpSpeed;
        }
        else
        {
            m_verticalSpeed -= gravity * Time.deltaTime;
        }

        Vector3 position = player.position + direction * moveSpeed * Time.deltaTime;
        position.y = Mathf.Max(GroundY, position.y + m_verticalSpeed * Time.deltaTime);
        position.x = Mathf.Clamp(position.x, -arenaSize, arenaSize);
        position.z = Mathf.Clamp(position.z, -arenaSize, arenaSize);
        player.position = position;
    }

    // The client sends the state of its player to the host
    void SendLocalPlayer()
    {
        if (m_client == null || !m_players.ContainsKey(m_localId)) return;

        Transform player = m_players[m_localId];
        TransformDTO dto = new TransformDTO();
        dto.objectId = m_localId;
        dto.x = player.position.x;
        dto.y = player.position.y;
        dto.z = player.position.z;
        dto.rotationY = player.eulerAngles.y;

        m_client.SendTransform(dto);
    }
}
