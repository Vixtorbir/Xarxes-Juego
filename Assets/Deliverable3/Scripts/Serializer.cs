using System.Collections.Generic;
using System.IO;

public static class Serializer
{
    public static byte[] Serialize(TransformDTO dto)
    {
        MemoryStream stream = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(stream);

        writer.Write(dto.objectId);
        writer.Write(dto.x);
        writer.Write(dto.y);
        writer.Write(dto.z);
        writer.Write(dto.rotationY);

        return stream.ToArray();
    }

    // Same order and same types as Serialize, the bytes do not say what they are
    public static TransformDTO DeserializeTransformDTO(byte[] data)
    {
        BinaryReader reader = new BinaryReader(new MemoryStream(data));
        TransformDTO dto = new TransformDTO();

        dto.objectId = reader.ReadInt32();
        dto.x = reader.ReadSingle();
        dto.y = reader.ReadSingle();
        dto.z = reader.ReadSingle();
        dto.rotationY = reader.ReadSingle();

        return dto;
    }

    public static byte[] Serialize(ShootDTO dto)
    {
        MemoryStream stream = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(stream);

        writer.Write(dto.playerId);
        writer.Write(dto.x);
        writer.Write(dto.y);
        writer.Write(dto.z);
        writer.Write(dto.dirX);
        writer.Write(dto.dirZ);

        return stream.ToArray();
    }

    public static ShootDTO DeserializeShootDTO(byte[] data)
    {
        BinaryReader reader = new BinaryReader(new MemoryStream(data));
        ShootDTO dto = new ShootDTO();

        dto.playerId = reader.ReadInt32();
        dto.x = reader.ReadSingle();
        dto.y = reader.ReadSingle();
        dto.z = reader.ReadSingle();
        dto.dirX = reader.ReadSingle();
        dto.dirZ = reader.ReadSingle();

        return dto;
    }

    // For a list, first how many, then each one
    public static byte[] Serialize(ScoreDTO dto)
    {
        MemoryStream stream = new MemoryStream();
        BinaryWriter writer = new BinaryWriter(stream);

        writer.Write(dto.round);
        writer.Write(dto.winnerId);
        writer.Write(dto.points.Count);
        foreach (int p in dto.points) writer.Write(p);
        writer.Write(dto.deaths.Count);
        foreach (int d in dto.deaths) writer.Write(d);

        return stream.ToArray();
    }

    public static ScoreDTO DeserializeScoreDTO(byte[] data)
    {
        BinaryReader reader = new BinaryReader(new MemoryStream(data));
        ScoreDTO dto = new ScoreDTO();
        dto.points = new List<int>();
        dto.deaths = new List<int>();

        dto.round = reader.ReadInt32();
        dto.winnerId = reader.ReadInt32();
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++) dto.points.Add(reader.ReadInt32());
        count = reader.ReadInt32();
        for (int i = 0; i < count; i++) dto.deaths.Add(reader.ReadInt32());

        return dto;
    }
}
