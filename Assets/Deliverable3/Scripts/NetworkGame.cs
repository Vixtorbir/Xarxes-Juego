using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Each PC moves its own player and sends its state (position and rotation) to the others.
// The other players just copy the last state that arrived for them.
public class NetworkGame : MonoBehaviour
{
    public float moveSpeed = 6f;
    public float jumpSpeed = 7f;
    public float gravity = 20f;
    public float arenaSize = 9f;
    public float sendInterval = 0.05f;   // 20 messages a second, not every frame
    public float smoothing = 15f;

    public GameObject playerPrefab;

    const float GroundY = 1f;   // the capsule is 2 units tall and its pivot is in the middle

    LobbyUDPServer m_server;
    LobbyUDPClient m_client;
    List<string> m_names;
    int m_localId;

    readonly Dictionary<int, Transform> m_players = new Dictionary<int, Transform>();
    readonly Dictionary<int, Vector3> m_targetPositions = new Dictionary<int, Vector3>();
    readonly Dictionary<int, float> m_targetRotations = new Dictionary<int, float>();
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
        MoveRemotePlayers();

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

    // We only get 20 positions a second, so we move towards the last one instead of teleporting.
    // A big jump means the player went back to its spawn, then we teleport too
    void MoveRemotePlayers()
    {
        foreach (int id in m_targetPositions.Keys)
        {
            Transform player = m_players[id];
            if (Vector3.Distance(player.position, m_targetPositions[id]) > 4f) player.position = m_targetPositions[id];
            else player.position = Vector3.Lerp(player.position, m_targetPositions[id], smoothing * Time.deltaTime);
            player.rotation = Quaternion.Slerp(player.rotation, Quaternion.Euler(0f, m_targetRotations[id], 0f), smoothing * Time.deltaTime);
        }
    }

    void SendLocalPlayer()
    {
        if (!m_players.ContainsKey(m_localId)) return;

        Transform player = m_players[m_localId];
        TransformDTO dto = new TransformDTO();
        dto.objectId = m_localId;
        dto.x = player.position.x;
        dto.y = player.position.y;
        dto.z = player.position.z;
        dto.rotationY = player.eulerAngles.y;

        if (m_server != null) m_server.SendTransform(dto);
        else m_client.SendTransform(dto);
    }

    // Called from the Update of the server or the client, so we are on the main thread
    public void OnTransformReceived(TransformDTO dto)
    {
        if (dto.objectId == m_localId || !m_players.ContainsKey(dto.objectId)) return;

        m_targetPositions[dto.objectId] = new Vector3(dto.x, dto.y, dto.z);
        m_targetRotations[dto.objectId] = dto.rotationY;
    }
}
