using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Each PC moves its own player and sends its state (position and rotation) to the others.
// The other players just copy the last state that arrived for them.
// Shots: everybody sees the bullets, but only the host decides who was hit and keeps the score.
public class NetworkGame : MonoBehaviour
{
    public float moveSpeed = 6f;
    public float jumpSpeed = 7f;
    public float gravity = 20f;
    public float arenaSize = 9f;
    public float sendInterval = 0.05f;   // 20 messages a second, not every frame
    public float smoothing = 15f;

    public float shootCooldown = 0.4f;
    public float bulletSpeed = 15f;
    public float bulletLifetime = 2f;
    public float hitRadius = 0.7f;
    public int pointsToWin = 3;
    public float scoreInterval = 0.5f;

    public GameObject playerPrefab;
    public GameObject bulletPrefab;
    public GameObject nameLabelPrefab;
    public Transform nameLabelParent;    // inside the Canvas, so the names are drawn under the panels
    public Material[] playerMaterials;   // one color per player id

    public TMP_Text goalText;
    public TMP_Text scoreText;
    public TMP_Text leaveButtonText;
    public GameObject winPanel;
    public TMP_Text winText;

    const float GroundY = 1f;   // the capsule is 2 units tall and its pivot is in the middle

    // The camera is set for 16:9 with 60 degrees. The Canvas scales the same way (1280x720, Expand)
    const float RefAspect = 16f / 9f;
    const float RefFov = 60f;

    // First the 4 corners, then the middle of each side
    static readonly Vector2[] Spawns =
    {
        new Vector2(-7f, -7f), new Vector2(7f, 7f), new Vector2(7f, -7f), new Vector2(-7f, 7f),
        new Vector2(0f, -7f), new Vector2(0f, 7f), new Vector2(-7f, 0f), new Vector2(7f, 0f),
    };

    class Bullet
    {
        public Transform transform;
        public Vector3 direction;
        public int ownerId;
        public float timeLeft;
    }

    LobbyUDPServer m_server;
    LobbyUDPClient m_client;
    List<string> m_names;
    int m_localId;

    readonly Dictionary<int, Transform> m_players = new Dictionary<int, Transform>();
    readonly Dictionary<int, Transform> m_labels = new Dictionary<int, Transform>();
    readonly Dictionary<int, Vector3> m_targetPositions = new Dictionary<int, Vector3>();
    readonly Dictionary<int, float> m_targetRotations = new Dictionary<int, float>();
    readonly List<Bullet> m_bullets = new List<Bullet>();
    float m_verticalSpeed;
    float m_sendTimer;
    float m_nextShot;

    ScoreDTO m_score;
    int m_myDeaths;

    readonly Dictionary<int, float> m_lastShot = new Dictionary<int, float>();
    float m_scoreTimer;

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

        m_score = new ScoreDTO();
        m_score.winnerId = -1;
        m_score.points = new List<int>();
        m_score.deaths = new List<int>();
        for (int id = 0; id < m_names.Count; id++)
        {
            m_score.points.Add(0);
            m_score.deaths.Add(0);
            SpawnPlayer(id);
        }

        goalText.text = "First to " + pointsToWin + " points wins";
        leaveButtonText.text = m_server != null ? "End game" : "Leave";
        UpdateHud();
    }

    Vector3 SpawnPosition(int id)
    {
        Vector2 spawn = Spawns[id % Spawns.Length];
        return new Vector3(spawn.x, GroundY, spawn.y);
    }

    Quaternion SpawnRotation(int id)
    {
        Vector3 toCenter = -SpawnPosition(id);
        toCenter.y = 0f;
        return Quaternion.LookRotation(toCenter);
    }

    void SpawnPlayer(int id)
    {
        GameObject player = Instantiate(playerPrefab, SpawnPosition(id), SpawnRotation(id));
        player.name = "Player " + id + " " + m_names[id];
        player.GetComponent<Renderer>().sharedMaterial = playerMaterials[id % playerMaterials.Length];

        GameObject label = Instantiate(nameLabelPrefab, nameLabelParent);
        TMP_Text labelText = label.GetComponentInChildren<TMP_Text>();
        labelText.text = m_names[id];
        if (id == m_localId) labelText.text += " (you)";

        m_players[id] = player.transform;
        m_labels[id] = label.transform;
    }

    void Update()
    {
        if (m_names == null) return;

        FitCamera();

        bool playing = m_score.winnerId < 0;
        if (playing && m_players.ContainsKey(m_localId))
        {
            MoveLocalPlayer();
            TryShoot();
        }
        MoveRemotePlayers();
        MoveBullets();

        if (m_server != null) UpdateHostScore();
        UpdateHud();

        m_sendTimer += Time.deltaTime;
        if (m_sendTimer >= sendInterval)
        {
            m_sendTimer = 0f;
            SendLocalPlayer();
        }
    }

    // Each name goes on the screen just above the head of its player
    void LateUpdate()
    {
        foreach (int id in m_labels.Keys)
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(m_players[id].position + Vector3.up * 1.4f);
            m_labels[id].gameObject.SetActive(screen.z > 0f);
            m_labels[id].position = new Vector3(screen.x, screen.y, 0f);
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

    void TryShoot()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        bool overButton = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool pressed = (keyboard != null && keyboard.fKey.wasPressedThisFrame) || (mouse != null && mouse.leftButton.wasPressedThisFrame && !overButton);
        if (!pressed || Time.time < m_nextShot) return;
        m_nextShot = Time.time + shootCooldown;

        Transform player = m_players[m_localId];
        ShootDTO dto = new ShootDTO();
        dto.playerId = m_localId;
        dto.x = player.position.x;
        dto.y = player.position.y;
        dto.z = player.position.z;
        dto.dirX = player.forward.x;
        dto.dirZ = player.forward.z;

        if (m_server != null)
        {
            if (OnShootReceived(dto)) m_server.SendShoot(dto);
        }
        else
        {
            SpawnBullet(dto);
            m_client.SendShoot(dto);
        }
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

    // Returns false if the shot is not valid. Only the host checks the cooldown,
    // so a modified client cannot shoot faster than the others
    public bool OnShootReceived(ShootDTO dto)
    {
        if (!m_players.ContainsKey(dto.playerId) || m_score.winnerId >= 0) return false;

        if (m_server != null)
        {
            if (m_lastShot.ContainsKey(dto.playerId) && Time.time - m_lastShot[dto.playerId] < shootCooldown * 0.8f) return false;
            m_lastShot[dto.playerId] = Time.time;
        }

        SpawnBullet(dto);
        return true;
    }

    void SpawnBullet(ShootDTO dto)
    {
        Vector3 direction = new Vector3(dto.dirX, 0f, dto.dirZ).normalized;

        Vector3 position = new Vector3(dto.x, dto.y + 0.3f, dto.z) + direction * 0.6f;
        GameObject bullet = Instantiate(bulletPrefab, position, Quaternion.identity);
        bullet.GetComponent<Renderer>().sharedMaterial = playerMaterials[dto.playerId % playerMaterials.Length];

        Bullet b = new Bullet();
        b.transform = bullet.transform;
        b.direction = direction;
        b.ownerId = dto.playerId;
        b.timeLeft = bulletLifetime;
        m_bullets.Add(b);
    }

    // Every PC moves the bullets so they can be seen, but only the host counts the hits
    void MoveBullets()
    {
        for (int i = m_bullets.Count - 1; i >= 0; i--)
        {
            Bullet b = m_bullets[i];
            b.transform.position += b.direction * bulletSpeed * Time.deltaTime;
            b.timeLeft -= Time.deltaTime;

            int hitId = HitPlayer(b);
            if (hitId >= 0 && m_server != null) OnHit(b.ownerId, hitId);

            Vector3 p = b.transform.position;
            bool outside = Mathf.Abs(p.x) > arenaSize + 1f || Mathf.Abs(p.z) > arenaSize + 1f;
            if (hitId >= 0 || outside || b.timeLeft <= 0f)
            {
                Destroy(b.transform.gameObject);
                m_bullets.RemoveAt(i);
            }
        }
    }

    int HitPlayer(Bullet b)
    {
        foreach (KeyValuePair<int, Transform> player in m_players)
        {
            if (player.Key == b.ownerId) continue;

            Vector3 offset = b.transform.position - player.Value.position;
            if (Mathf.Abs(offset.y) > 1.2f) continue;
            offset.y = 0f;
            if (offset.magnitude < hitRadius) return player.Key;
        }
        return -1;
    }

    void OnHit(int shooterId, int targetId)
    {
        m_score.points[shooterId]++;
        m_score.deaths[targetId]++;
        Debug.Log("[GAME] " + m_names[shooterId] + " hit " + m_names[targetId] + ", " + m_score.points[shooterId] + " points");

        if (m_score.points[shooterId] >= pointsToWin)
        {
            m_score.winnerId = shooterId;
            Debug.Log("[GAME] " + m_names[shooterId] + " wins");
        }
        SendScore();
    }

    // Host only. The score is sent again every scoreInterval seconds, in case one was lost
    void UpdateHostScore()
    {
        m_scoreTimer += Time.deltaTime;
        if (m_scoreTimer >= scoreInterval) SendScore();
    }

    void SendScore()
    {
        m_scoreTimer = 0f;
        OnScoreReceived(m_score);
        m_server.SendScore(m_score);
    }

    // More deaths than before: our player goes back to its spawn.
    // It works even if some Score messages were lost, because each one has the whole score
    public void OnScoreReceived(ScoreDTO score)
    {
        if (m_localId < score.deaths.Count && score.deaths[m_localId] > m_myDeaths)
        {
            m_myDeaths = score.deaths[m_localId];
            Respawn();
        }
        m_score = score;
    }

    void Respawn()
    {
        if (!m_players.ContainsKey(m_localId)) return;

        m_players[m_localId].position = SpawnPosition(m_localId);
        m_players[m_localId].rotation = SpawnRotation(m_localId);
        m_verticalSpeed = 0f;
    }

    public void RemovePlayer(int id)
    {
        if (!m_players.ContainsKey(id)) return;

        Destroy(m_players[id].gameObject);
        Destroy(m_labels[id].gameObject);
        m_players.Remove(id);
        m_labels.Remove(id);
        m_targetPositions.Remove(id);
        m_targetRotations.Remove(id);
    }

    // If the window is narrower than 16:9 we open the vertical angle, so the sides of the arena
    // are never cut. If it is wider we keep 60 degrees and just see a bit more on the sides
    void FitCamera()
    {
        Camera cam = Camera.main;
        float aspect = (float)Screen.width / Screen.height;

        if (aspect >= RefAspect)
        {
            cam.fieldOfView = RefFov;
            return;
        }

        float halfWidth = Mathf.Tan(RefFov * 0.5f * Mathf.Deg2Rad) * RefAspect;
        cam.fieldOfView = 2f * Mathf.Atan(halfWidth / aspect) * Mathf.Rad2Deg;
    }

    void UpdateHud()
    {
        string score = "";
        foreach (int id in m_players.Keys) score += m_names[id] + ": " + m_score.points[id] + "    ";
        scoreText.text = score;

        winPanel.SetActive(m_score.winnerId >= 0);
        if (m_score.winnerId >= 0) winText.text = m_names[m_score.winnerId] + " wins!";
    }

    public void OnLeaveButton()
    {
        if (m_server != null) m_server.EndGame();
        else m_client.LeaveGame();
    }
}
