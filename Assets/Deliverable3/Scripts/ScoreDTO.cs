using System;
using System.Collections.Generic;

// The whole score of the round. The host sends it again every time, so a lost one does not matter
[Serializable]
public struct ScoreDTO
{
    public int round;
    public int winnerId;          // -1 while nobody has won
    public List<int> points;      // index = player id
    public List<int> deaths;      // when ours goes up we were hit and we go back to our spawn
}
