# XJO - Project Deliverable: first iteration

Multiplayer arena game made for Xarxes per a Jocs Online. It starts from our Deliverable 2 lobby and the Lab 3 Serializer: the host presses Start, everybody goes to the game scene, and the players move, jump and shoot each other over UDP.

**Group:** Víctor Cárdenas Hormigo, Marc Amo Mateu, Noa Pont, Pau González

**Unity version:** 6000.3.16f1

## How to run it

1. Download `ProjectDeliverable_FirstIteration_Windows.zip` from the GitHub Release, unzip it and run `ProjectDeliverable_FirstIteration.exe`. Unity is not needed.
2. **Host:** in the menu, under **UDP**, press **Create game**, write a name and press **Create**. Look up your IP with `ipconfig` and tell it to the other players.
3. **Other players:** under **UDP**, press **Join game**, write a name and the host's IP, and press **Join**.
4. When at least one player is in the room, the host presses **Start** and everybody goes to the game.

To test on one PC, open the exe several times and join with `127.0.0.1`. The first time the exe runs, Windows asks to allow it through the firewall: accept it, or the other PC will not reach the host.

**Controls:** WASD / Arrow keys to move, Space to jump, F / Left click to shoot. The first player to reach 3 points wins the round, and a new round starts 4 seconds later. The host's **End game** button closes the game for everybody, and a client's **Leave** button takes only that player out.

The TCP column of the menu is the Deliverable 2 lobby and chat, kept as it was. The game itself only runs over UDP.

## Opening it in Unity

Open the repository folder with Unity 6000.3.16f1, or import `ProjectDeliverable_FirstIteration.unitypackage` into another project:

1. Create a new project in Unity Hub with the **Universal 3D** template. A **Built-in Render Pipeline** project works too.
2. Check that the project has the **uGUI** (`com.unity.ugui`) and **Input System** (`com.unity.inputsystem`) packages. The Unity Hub templates already include both. In a completely empty project, add them from the Package Manager, and press **Yes** when Unity asks to enable the new Input System.
3. **Assets > Import Package > Custom Package**, and import everything. The TextMesh Pro essentials are included.
4. In **File > Build Profiles**, add the six scenes of `Assets/Deliverable3/Scenes` to the scene list, with `S_Menu` first. The game loads its scenes by name, so they have to be in the list, even to play in the Editor.
5. Open `S_Menu` and press Play.

The materials use our own small shader, `Deliverable3/SimpleLit`, so the game looks the same in URP and in the Built-in Render Pipeline.

## Scenes

All the scenes are in `Assets/Deliverable3/Scenes`. **The main scene is `S_Menu`**, the first one in the build list.

| Scene | What it is |
| --- | --- |
| `S_Menu` | Main menu: TCP or UDP, create or join |
| `S_UDP_Create` / `S_UDP_Join` | UDP lobby and chat, host and client. The host has the Start button |
| `S_Game` | The game. It is loaded by the host when Start is pressed and by each client when the START message arrives |
| `S_TCP_Create` / `S_TCP_Join` | TCP lobby and chat from Deliverable 2 |

The lobby object (`LobbyUDPServer` or `LobbyUDPClient`) is kept alive with `DontDestroyOnLoad` when the game starts, so the game uses the same socket as the lobby. `NetworkGame` finds it in `Start()`.

## What we send

Every UDP message starts with one `MsgType` byte. Game data is binary, written with the `Serializer` (BinaryWriter / BinaryReader). Text is only used for the lobby and the chat.

| Type | Content | Size | Who sends it |
| --- | --- | --- | --- |
| `Text` (1) | a string: `JOIN:`, `CHAT:`, `PLAYERS:`, `START:`, `LEAVE:`, `LEFT:`, `END:`, `PING:` | - | lobby and control messages |
| `Transform` (2) | `TransformDTO`: id, x, y, z, rotationY | 1 + 20 bytes | every player, 20 times a second |
| `Shoot` (3) | `ShootDTO`: id, x, y, z, dirX, dirZ | 1 + 24 bytes | the player that shoots, once per shot |
| `Score` (4) | `ScoreDTO`: round, winner, points per player, deaths per player | 1 + 16 + 8 per player | the host, on every change and every 0.5 s |

Nothing from the network touches Unity from the network thread. The receive thread puts the bytes in a `ConcurrentQueue`, and `Update()` reads and handles them on the main thread. A message that cannot be read is logged and dropped.

### State, not input

Each player sends the **state** of its own player (position and rotation), not its input.

- **No delay for your own player.** Each PC moves its own player as soon as a key is pressed. With input, a client would have to wait for the host's answer before seeing itself move.
- **A lost packet does not matter.** Each Transform has the whole position, so the next one, 50 ms later, fixes everything. With input, a lost "W pressed" would leave the host and the client out of sync.
- **Simple.** The host does not need to simulate the clients' players. It shows the last state that arrived and sends the same bytes to the other clients.

The host checks that the id inside each Transform and Shoot is the id it gave to that address, so a client can only move and shoot with its own player. The host is also the only one that decides hits and points, so the score is the same for everybody.

Shots are sent as an event (a `ShootDTO`), because a shot happens once and is not part of the state. The score is sent as state: the whole `ScoreDTO` every time.

## Extras

- **Full movement over the network:** moving, jumping and turning to face where you walk. Remote players are smoothed with a Lerp between the positions that arrive. If the jump is bigger than 4 units, for example after a respawn, they teleport.
- **Shooting:** F or left click fires a bullet that everybody sees. The host checks the cooldown, moves the bullets, detects hits and keeps the score. A player that is hit goes back to its spawn.
- **More than 2 players:** the host sends every Transform and Shoot it receives to all the other clients, so everybody sees everybody. Each player has its own color, spawn corner and name over its head.
- **A full game:** first to 3 points wins. A win panel shows the winner, and after 4 seconds the host starts a new round with all the points at 0.
- **Leaving and lost players:** if a client leaves or stops sending for 5 seconds, the host removes it and tells the others with `LEFT:`. If the host leaves or disappears, the clients go back to the menu.
- **Lag and reliability:** explained below.

### How we keep the lag low

- **Your player moves at once.** Each PC moves its own player without asking the host, and then sends the new position.
- **Small messages.** A position is only 21 bytes in binary.
- **20 messages a second.** It is enough because the other players move smoothly towards each new position.
- **Your bullet appears at once.** The shooter does not wait for the host.
- **UDP does not wait for lost packets.** If a position is lost, a newer one arrives 50 ms later.

### How we make sure the important messages arrive

UDP can lose messages, so the important ones are sent in a way that losing one does not break the game:

- **START** is sent again every second until that client starts sending its position.
- **Score** always has the full score and is sent every 0.5 s, so if one is lost, the next one fixes it.
- **END** and **LEAVE** can be lost, so if nothing arrives from a player for 5 seconds, it counts as gone.
- **Positions** are never sent again, because the next one replaces them.

## Known bugs

- **Repeated names are not checked.** Two players can use the same name.
- **A second host on the same PC fails silently.** Port 9050 is already in use. The error only shows in the console, and the second room looks open but never receives anything.
- **A lost UDP host is only noticed in the game.** In the lobby the host does not send anything regularly, so if it closes on another PC the client stays in the room. The same happens if the IP is right but nobody is hosting. The client has to press Leave.
- **A lost Shoot packet is lost.** Shots are not repeated. If one is lost, that bullet is not seen by the others and the hit does not count, even though the shooter saw it. The same happens if the host rejects the shot (cooldown, or the round has just ended).
- **A bullet can still score after the round is won.** The host does not check if the round already has a winner when a bullet hits. If a bullet is still flying when someone reaches 3 points and it hits, its shooter also gets a point, and if that is their third point the winner changes. For example, "Victor wins!" with 3-2 can turn into "Alex wins!" with 3-3.
- **Hits are decided with the host's view.** The host sees the other players a few milliseconds late because of the smoothing. A shot that looks like a hit on a client's screen can miss, and the other way round.
- **Movement is not checked by the host.** It only checks that a client moves its own player. A modified client could move faster or teleport.
- **No player limit.** With more than 6 players the colors repeat, and with more than 8 the spawn points repeat.

## What changed since Deliverable 2

- All the UDP messages now start with a `MsgType` byte (Lab 3). Lobby messages are `Text` messages written with `BinaryWriter`, no longer raw `Encoding.UTF8` bytes. Game data is binary: `Transform`, `Shoot` and `Score`.
- New files: `Serializer`, `Messages`, `MsgType`, `TransformDTO`, `ShootDTO`, `ScoreDTO` and `NetworkGame`, plus the `S_Game` scene and the `Player`, `Bullet` and `NameLabel` prefabs.
- The UDP lobby has a **Start** button. The host gives each player an id (its position in the list, the host is 0) and sends `START:<yourId>:<names>`.
- Once the game has started, the host ignores new `JOIN` and `CHAT` messages, and a player that leaves is announced with `LEFT:<id>`.
- A message that cannot be read is dropped instead of breaking the lobby.
- The client now notices a lost host in the game (5 second timeout), which was a known bug in Deliverable 2.
- Commas are removed from the player names, in TCP and UDP. The player list is separated by commas, so a name with a comma used to show up as two players, another known bug of Deliverable 2.
- Apart from the names, the TCP lobby is the same as in Deliverable 2.
- The project was moved to a new repository, with the code in `Assets/Deliverable3`.

## Who did what

| Member | Work |
| --- | --- |
| **Víctor Cárdenas Hormigo** | Project setup. Lab 3 Serializer and the binary message format (`MsgType`, `Messages`, `TransformDTO`). Brought the Deliverable 2 lobby and chat into the project. Start button: player ids, `START` message and the game scene. README, build and release. |
| **Marc Amo Mateu** | `Player` prefab. Moving and jumping the local player and sending its state to the host (client to server). Showing the client's player on the host, checking that each client only moves its own player. |
| **Noa Pont** | Sending the host's player to the clients and relaying every Transform to all the players (server to client, more than 2 players). Smoothing of the remote players. Spawn corners and one color per player. HUD: names over the players, controls and Leave button. Players that leave and a lost host. |
| **Pau González** | `Shoot` and `Score` messages. Shooting over the network, checked by the host. Hits, points and the win condition. New round after a win. |
